using System.Drawing;
using System.Drawing.Imaging;
using WordSearchBook.Core.WordSearch.Application;
using WordSearchBook.Core.WordSearch.Contracts;

namespace WordSearchBook.Infrastructure.WordSearch.Processing;

public sealed class SystemDrawingBookAnswerBatchExporter : IBookAnswerBatchExporter
{
    private const int PageWidth = 2588;
    private const int PageHeight = 3375;
    private const int OutputDpi = 300;
    private const long AnswerJpegQuality = 85L;

    public async Task<IReadOnlyList<PreparedBookAnswer>> PrepareAsync(
        string cacheDirectory,
        IReadOnlyList<GeneratedWordSearchTopic> topics,
        string outputDirectory,
        int maximumConcurrency,
        IProgress<BookProcessingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        ArgumentNullException.ThrowIfNull(topics);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumConcurrency, 1);
        var duplicate = topics.GroupBy(topic => topic.Index).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new WordSearchGenerationException(
                "answer_work_key_duplicate",
                $"Answer work key 'answer:{duplicate.Key}' is duplicated.");
        }

        var orderedTopics = topics.OrderBy(topic => topic.Index).ToArray();
        var answerDirectory = Path.Combine(outputDirectory, "answer");
        Directory.CreateDirectory(answerDirectory);
        var results = new PreparedBookAnswer?[orderedTopics.Length];
        var pendingPaths = orderedTopics
            .Select(topic => Path.Combine(answerDirectory, $".{topic.Index:000}.jpg.pending"))
            .ToArray();
        foreach (var pendingPath in pendingPaths)
        {
            TryDeleteFile(pendingPath);
        }

        using var semaphore = new SemaphoreSlim(maximumConcurrency, maximumConcurrency);
        using var remainingWorkCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var completed = 0;
        var work = orderedTopics.Select((topic, position) => Task.Run(
            () => ProcessOneAsync(topic, position),
            CancellationToken.None)).ToArray();
        progress?.Report(new BookProcessingProgress("Exporting answer JPEG files", 0, orderedTopics.Length));

        try
        {
            try
            {
                await Task.WhenAll(work);
            }
            catch
            {
                await remainingWorkCancellation.CancelAsync();
                try
                {
                    await Task.WhenAll(work);
                }
                catch
                {
                    // Preserve the first worker failure observed by the caller.
                }

                throw;
            }

            return results.Select(result => result!).ToArray();
        }
        catch
        {
            foreach (var pendingPath in pendingPaths)
            {
                TryDeleteFile(pendingPath);
            }

            throw;
        }

        async Task ProcessOneAsync(GeneratedWordSearchTopic topic, int position)
        {
            var entered = false;
            try
            {
                await semaphore.WaitAsync(remainingWorkCancellation.Token);
                entered = true;
                var answerArtifact = topic.Artifacts.Single(artifact => artifact.Kind == WordSearchArtifactKind.PageAnswer);
                var source = Path.Combine(cacheDirectory, answerArtifact.RelativePath);
                var fileName = $"{topic.Index:000}.jpg";
                var finalPath = Path.Combine(answerDirectory, fileName);
                var pendingPath = pendingPaths[position];
                ExportAnswerJpeg(source, pendingPath);
                var info = new FileInfo(pendingPath);
                results[position] = new PreparedBookAnswer(
                    new BookAnswerOutput(
                        topic.Index,
                        $"answer/{fileName}",
                        info.Length,
                        PageWidth,
                        PageHeight,
                        (int)AnswerJpegQuality),
                    pendingPath,
                    finalPath);
                var count = Interlocked.Increment(ref completed);
                progress?.Report(new BookProcessingProgress(
                    "Exporting answer JPEG files",
                    count,
                    orderedTopics.Length,
                    $"Topic {topic.Index}: {topic.Name}"));
            }
            catch (OperationCanceledException)
            {
                await remainingWorkCancellation.CancelAsync();
                throw;
            }
            catch (WordSearchGenerationException)
            {
                await remainingWorkCancellation.CancelAsync();
                throw;
            }
            catch (Exception exception) when (exception is not WordSearchGenerationException)
            {
                await remainingWorkCancellation.CancelAsync();
                throw new WordSearchGenerationException(
                    "answer_export_failed",
                    $"Topic {topic.Index} '{topic.Name}' answer JPEG could not be exported: {exception.Message}",
                    exception);
            }
            finally
            {
                if (entered)
                {
                    semaphore.Release();
                }
            }
        }
    }

    private static void ExportAnswerJpeg(string sourcePath, string targetPath)
    {
        using var source = Image.FromFile(sourcePath);
        if (source.Width != PageWidth || source.Height != PageHeight)
        {
            throw new InvalidDataException($"Answer page must be {PageWidth} x {PageHeight} pixels: {sourcePath}");
        }

        using (var output = new Bitmap(PageWidth, PageHeight, PixelFormat.Format24bppRgb))
        {
            output.SetResolution(OutputDpi, OutputDpi);
            using var graphics = Graphics.FromImage(output);
            graphics.Clear(Color.White);
            graphics.DrawImageUnscaled(source, 0, 0);
            var codec = ImageCodecInfo.GetImageEncoders().Single(encoder => encoder.FormatID == ImageFormat.Jpeg.Guid);
            using var parameters = new EncoderParameters(1);
            parameters.Param[0] = new EncoderParameter(Encoder.Quality, AnswerJpegQuality);
            output.Save(targetPath, codec, parameters);
        }

        using var verification = Image.FromFile(targetPath);
        if (verification.RawFormat.Guid != ImageFormat.Jpeg.Guid ||
            verification.Width != PageWidth ||
            verification.Height != PageHeight)
        {
            throw new InvalidDataException($"Answer JPEG did not pass output verification: {targetPath}");
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A later run will retry the stable pending path.
        }
    }
}
