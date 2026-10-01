using WordSearchBook.Core.WordSearch.Application;
using WordSearchBook.Core.WordSearch.Contracts;

namespace WordSearchBook.Infrastructure.WordSearch.Processing;

public sealed class FileSystemBookOutputPublisher : IBookOutputPublisher
{
    private const int MaximumAccessAttempts = 5;
    private static readonly TimeSpan AccessRetryDelay = TimeSpan.FromMilliseconds(500);

    public async Task PublishAsync(
        BookOutputPublicationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateUniqueTargets(request);
        ValidatePendingFiles(request);

        Directory.CreateDirectory(request.OutputDirectory);
        var answerDirectory = Path.Combine(request.OutputDirectory, "answer");
        Directory.CreateDirectory(answerDirectory);
        var expectedAnswerPaths = request.Answers
            .Select(answer => Path.GetFullPath(answer.FinalPath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var obsoleteAnswers = Directory.EnumerateFiles(answerDirectory, "*.jpg", SearchOption.TopDirectoryOnly)
            .Where(path => !expectedAnswerPaths.Contains(Path.GetFullPath(path)))
            .ToArray();
        var finalTargets = request.Answers.Select(answer => answer.FinalPath)
            .Append(request.Pdf.FinalPath)
            .Append(request.ManifestPath)
            .Concat(obsoleteAnswers)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var lockedPath = await WaitForExclusiveAccessAsync(finalTargets, cancellationToken);
        if (lockedPath is not null)
        {
            throw new WordSearchGenerationException(
                "output_file_in_use",
                $"Output file is currently open and could not be replaced: {lockedPath}. Close the PDF or image viewer, then process the Book again.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            foreach (var answer in request.Answers.OrderBy(answer => answer.Output.TopicIndex))
            {
                File.Move(answer.PendingPath, answer.FinalPath, overwrite: true);
            }

            File.Move(request.Pdf.PendingPath, request.Pdf.FinalPath, overwrite: true);
            foreach (var obsoleteAnswer in obsoleteAnswers)
            {
                File.Delete(obsoleteAnswer);
            }

            File.Move(request.PendingManifestPath, request.ManifestPath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new WordSearchGenerationException(
                "output_publish_failed",
                $"Output files could not be replaced: {exception.Message}",
                exception);
        }
    }

    private static void ValidateUniqueTargets(BookOutputPublicationRequest request)
    {
        var targets = request.Answers.Select(answer => answer.FinalPath)
            .Append(request.Pdf.FinalPath)
            .Append(request.ManifestPath)
            .Select(Path.GetFullPath)
            .ToArray();
        var duplicate = targets.GroupBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new WordSearchGenerationException(
                "output_work_key_duplicate",
                $"Multiple output workers target the same file: {duplicate.Key}");
        }
    }

    private static void ValidatePendingFiles(BookOutputPublicationRequest request)
    {
        var missing = request.Answers.Select(answer => answer.PendingPath)
            .Append(request.Pdf.PendingPath)
            .Append(request.PendingManifestPath)
            .FirstOrDefault(path => !File.Exists(path));
        if (missing is not null)
        {
            throw new WordSearchGenerationException(
                "output_pending_missing",
                $"Prepared output file is missing: {missing}");
        }
    }

    private static async Task<string?> WaitForExclusiveAccessAsync(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken)
    {
        string? inaccessible = null;
        for (var attempt = 1; attempt <= MaximumAccessAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            inaccessible = paths.FirstOrDefault(path => !CanOpenExclusively(path));
            if (inaccessible is null)
            {
                return null;
            }

            if (attempt < MaximumAccessAttempts)
            {
                await Task.Delay(AccessRetryDelay, cancellationToken);
            }
        }

        return inaccessible;
    }

    private static bool CanOpenExclusively(string path)
    {
        if (!File.Exists(path)) return true;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
