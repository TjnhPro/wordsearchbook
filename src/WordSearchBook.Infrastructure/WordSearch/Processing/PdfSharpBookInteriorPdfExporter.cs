using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using WordSearchBook.Core.WordSearch.Application;
using WordSearchBook.Core.WordSearch.Contracts;

namespace WordSearchBook.Infrastructure.WordSearch.Processing;

public sealed class PdfSharpBookInteriorPdfExporter : IBookInteriorPdfExporter
{
    private const int PageWidth = 2588;
    private const int PageHeight = 3375;
    private const int OutputDpi = 300;
    private const double PageWidthPoints = PageWidth * 72d / OutputDpi;
    private const double PageHeightPoints = PageHeight * 72d / OutputDpi;

    public async Task<PreparedInteriorPdf> PrepareAsync(
        IReadOnlyList<string> orderedPages,
        string workDirectory,
        string pendingPath,
        string finalPath,
        int maximumConcurrency,
        IProgress<BookProcessingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderedPages);
        ArgumentException.ThrowIfNullOrWhiteSpace(workDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(pendingPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(finalPath);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumConcurrency, 1);
        if (orderedPages.Count == 0)
        {
            throw new WordSearchGenerationException(
                "pdf_pages_empty",
                "At least one puzzle page is required for PDF export.");
        }

        PrepareWorkDirectory(workDirectory);
        DeleteFileIfExists(pendingPath);
        var unitPaths = Enumerable.Range(0, orderedPages.Count)
            .Select(index => Path.Combine(workDirectory, $"{index:0000}.pdf"))
            .ToArray();
        using var semaphore = new SemaphoreSlim(maximumConcurrency, maximumConcurrency);
        using var remainingWorkCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var completed = 0;
        var work = orderedPages.Select((sourcePath, index) => Task.Run(
            () => ProcessOneAsync(sourcePath, index),
            CancellationToken.None)).ToArray();
        progress?.Report(new BookProcessingProgress("Assembling interior PDF", 0, orderedPages.Count));

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

            cancellationToken.ThrowIfCancellationRequested();
            using (var finalDocument = new PdfDocument())
            {
                foreach (var unitPath in unitPaths)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var unit = PdfReader.Open(unitPath, PdfDocumentOpenMode.Import);
                    finalDocument.Pages.InsertRange(finalDocument.Pages.Count, unit);
                }

                finalDocument.Save(pendingPath);
            }

            VerifyPdf(pendingPath, orderedPages.Count);
            return new PreparedInteriorPdf(
                pendingPath,
                finalPath,
                new FileInfo(pendingPath).Length,
                orderedPages.Count);
        }
        catch (OperationCanceledException)
        {
            TryDeleteFile(pendingPath);
            throw;
        }
        catch (WordSearchGenerationException)
        {
            TryDeleteFile(pendingPath);
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            TryDeleteFile(pendingPath);
            throw new WordSearchGenerationException(
                "pdf_export_failed",
                $"Interior PDF could not be assembled: {exception.Message}",
                exception);
        }
        finally
        {
            TryDeleteDirectory(workDirectory);
        }

        async Task ProcessOneAsync(string sourcePath, int index)
        {
            var entered = false;
            try
            {
                await semaphore.WaitAsync(remainingWorkCancellation.Token);
                entered = true;
                CreatePageUnit(sourcePath, unitPaths[index], remainingWorkCancellation.Token);
                var count = Interlocked.Increment(ref completed);
                progress?.Report(new BookProcessingProgress(
                    "Assembling interior PDF",
                    count,
                    orderedPages.Count,
                    $"Page {index + 1}"));
            }
            catch
            {
                await remainingWorkCancellation.CancelAsync();
                throw;
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

    private static void CreatePageUnit(string sourcePath, string targetPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var image = XImage.FromFile(sourcePath);
        if (image.PixelWidth != PageWidth || image.PixelHeight != PageHeight)
        {
            throw new InvalidDataException($"PDF source page must be {PageWidth} x {PageHeight} pixels: {sourcePath}");
        }

        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(PageWidthPoints);
        page.Height = XUnit.FromPoint(PageHeightPoints);
        using (var graphics = XGraphics.FromPdfPage(page))
        {
            graphics.DrawImage(image, 0, 0, PageWidthPoints, PageHeightPoints);
        }

        cancellationToken.ThrowIfCancellationRequested();
        document.Save(targetPath);
    }

    private static void VerifyPdf(string path, int expectedPageCount)
    {
        using var document = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        if (document.PageCount != expectedPageCount)
        {
            throw new InvalidDataException($"Interior PDF contains {document.PageCount} pages; expected {expectedPageCount}.");
        }

        foreach (var page in document.Pages)
        {
            if (Math.Abs(page.Width.Point - PageWidthPoints) > 0.01 ||
                Math.Abs(page.Height.Point - PageHeightPoints) > 0.01)
            {
                throw new InvalidDataException("Interior PDF contains a page with invalid physical dimensions.");
            }
        }
    }

    private static void PrepareWorkDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }

        Directory.CreateDirectory(path);
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            DeleteFileIfExists(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A later run will retry the stable pending path.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A later run clears the fixed PDF work directory before processing.
        }
    }
}
