using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text.Json;
using WordSearchBook.Core.WordSearch.Application;
using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Core.WordSearch.Validation;

namespace WordSearchBook.Infrastructure.WordSearch.Processing;

public sealed class BookProcessingService(
    IWordSearchBookGenerationService generationService,
    IBookDataValidationService dataValidationService,
    IBrandValidationService brandValidationService) : IBookProcessingService
{
    private const int ManifestSchemaVersion = 1;
    private const int PageWidth = 2588;
    private const int PageHeight = 3375;
    private const int OutputDpi = 300;
    private const long AnswerJpegQuality = 85L;
    private const double PageWidthPoints = PageWidth * 72d / OutputDpi;
    private const double PageHeightPoints = PageHeight * 72d / OutputDpi;
    private static readonly HashSet<string> SupportedImageExtensions =
        new([".png", ".jpg", ".jpeg"], StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<BookProcessingResult> ProcessAsync(
        BookProcessingRequest request,
        IProgress<BookProcessingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var rootPath = Path.GetFullPath(RequireValue(request.RootPath, nameof(request.RootPath)));
        ValidatePathSegment(request.BookId, nameof(request.BookId));
        ValidatePathSegment(request.BrandId, nameof(request.BrandId));
        if (!Directory.Exists(rootPath))
        {
            throw new WordSearchGenerationException("root_not_found", $"Root directory was not found: {rootPath}");
        }

        var bookDirectory = ResolveChildDirectory(Path.Combine(rootPath, "input"), request.BookId, "book_not_found");
        var brandDirectory = ResolveChildDirectory(Path.Combine(rootPath, "brands"), request.BrandId, "brand_not_found");
        var dataPath = Path.Combine(bookDirectory, "data.csv");
        if (!File.Exists(dataPath))
        {
            throw new WordSearchGenerationException("input_not_found", $"CSV input was not found: {dataPath}");
        }

        var workspaceDirectory = Path.Combine(bookDirectory, ".workspace");
        Directory.CreateDirectory(workspaceDirectory);
        var stagingDirectory = Path.Combine(workspaceDirectory, $".output-staging-{Guid.NewGuid():N}");
        var temporaryManifestPath = Path.Combine(workspaceDirectory, $".output-manifest-{Guid.NewGuid():N}.tmp");

        try
        {
            progress?.Report(new BookProcessingProgress("Verifying certified input"));
            await using var dataLock = new FileStream(
                dataPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var dataHash = $"sha256:{Convert.ToHexStringLower(await SHA256.HashDataAsync(dataLock, cancellationToken))}";
            var dataState = await dataValidationService.CheckStateAsync(rootPath, request.BookId, cancellationToken);
            if (dataState.Status != BookDataValidationStatus.Validated ||
                !string.Equals(dataState.ContentHash, dataHash, StringComparison.Ordinal))
            {
                throw new WordSearchGenerationException(
                    "book_data_not_validated",
                    "data.csv is not currently certified. Validate the CSV before processing.");
            }

            var brandState = await brandValidationService.CheckStateAsync(rootPath, request.BrandId, cancellationToken);
            if (brandState.Status != BrandValidationStatus.Validated)
            {
                throw new WordSearchGenerationException(
                    "brand_not_validated",
                    $"Brand '{request.BrandId}' assets must be validated before processing.");
            }

            var settingsSignature = await CalculateSettingsSignatureAsync(rootPath, request.BrandId, cancellationToken);
            progress?.Report(new BookProcessingProgress("Generating puzzle and answer pages"));
            var generation = await generationService.GenerateAsync(
                new WordSearchGenerationRequest(rootPath, request.BookId, request.BrandId),
                cancellationToken);

            Directory.CreateDirectory(stagingDirectory);
            var stagingAnswerDirectory = Path.Combine(stagingDirectory, "answer");
            Directory.CreateDirectory(stagingAnswerDirectory);
            var cacheDirectory = Path.GetDirectoryName(generation.ManifestPath)!;
            var answers = new List<BookAnswerOutput>(generation.Topics.Count);
            progress?.Report(new BookProcessingProgress("Exporting answer JPEG files", 0, generation.Topics.Count));
            foreach (var topic in generation.Topics.OrderBy(topic => topic.Index))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var answerArtifact = topic.Artifacts.Single(artifact => artifact.Kind == WordSearchArtifactKind.PageAnswer);
                var source = Path.Combine(cacheDirectory, answerArtifact.RelativePath);
                var fileName = $"{topic.Index:000}.jpg";
                var target = Path.Combine(stagingAnswerDirectory, fileName);
                ExportAnswerJpeg(source, target);
                var info = new FileInfo(target);
                answers.Add(new BookAnswerOutput(
                    topic.Index,
                    $"answer/{fileName}",
                    info.Length,
                    PageWidth,
                    PageHeight,
                    (int)AnswerJpegQuality));
                progress?.Report(new BookProcessingProgress(
                    "Exporting answer JPEG files",
                    answers.Count,
                    generation.Topics.Count,
                    topic.Name));
            }

            var frontPages = DiscoverOrderedPages(Path.Combine(brandDirectory, "front"));
            var puzzlePages = generation.Topics
                .OrderBy(topic => topic.Index)
                .Select(topic => topic.Artifacts.Single(artifact => artifact.Kind == WordSearchArtifactKind.Page))
                .Select(artifact => Path.Combine(cacheDirectory, artifact.RelativePath))
                .ToArray();
            var backPages = DiscoverOrderedPages(Path.Combine(brandDirectory, "back"));
            var orderedPdfPages = frontPages.Concat(puzzlePages).Concat(backPages).ToArray();
            var pdfFileName = $"{request.BookId}.interior.pdf";
            var stagingPdfPath = Path.Combine(stagingDirectory, pdfFileName);
            progress?.Report(new BookProcessingProgress("Assembling interior PDF", 0, orderedPdfPages.Length));
            WritePdf(stagingPdfPath, orderedPdfPages, progress, cancellationToken);
            VerifyPdf(stagingPdfPath, orderedPdfPages.Length);

            progress?.Report(new BookProcessingProgress("Verifying output"));
            var currentBrandState = await brandValidationService.CheckStateAsync(rootPath, request.BrandId, cancellationToken);
            if (currentBrandState.Status != BrandValidationStatus.Validated ||
                !string.Equals(currentBrandState.Fingerprint, brandState.Fingerprint, StringComparison.Ordinal))
            {
                throw new WordSearchGenerationException(
                    "brand_changed_during_processing",
                    "Brand assets changed while the book was being processed.");
            }

            var currentSettingsSignature = await CalculateSettingsSignatureAsync(rootPath, request.BrandId, cancellationToken);
            if (!string.Equals(currentSettingsSignature, settingsSignature, StringComparison.Ordinal))
            {
                throw new WordSearchGenerationException(
                    "settings_changed_during_processing",
                    "Settings changed while the book was being processed.");
            }

            var processedAt = DateTimeOffset.UtcNow;
            var pdfInfo = new FileInfo(stagingPdfPath);
            var manifest = new OutputManifest(
                ManifestSchemaVersion,
                request.BookId,
                request.BrandId,
                dataHash,
                brandState.Fingerprint!,
                settingsSignature,
                processedAt,
                generation.Topics.Count,
                frontPages.Count,
                backPages.Count,
                new PdfManifest(pdfFileName, pdfInfo.Length, orderedPdfPages.Length, PageWidth, PageHeight, OutputDpi),
                answers);
            await WriteManifestAsync(temporaryManifestPath, manifest, cancellationToken);

            progress?.Report(new BookProcessingProgress("Publishing output"));
            var outputDirectory = Path.Combine(bookDirectory, "output");
            var manifestPath = Path.Combine(workspaceDirectory, "output.manifest.json");
            PublishAtomically(stagingDirectory, outputDirectory, temporaryManifestPath, manifestPath);

            return new BookProcessingResult(
                request.BookId,
                request.BrandId,
                Path.Combine(outputDirectory, pdfFileName),
                Path.Combine(outputDirectory, "answer"),
                generation.Topics.Count,
                frontPages.Count,
                backPages.Count,
                orderedPdfPages.Length,
                pdfInfo.Length,
                answers,
                processedAt);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (WordSearchGenerationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            throw new WordSearchGenerationException(
                "book_processing_failed",
                $"Book processing failed: {exception.Message}",
                exception);
        }
        finally
        {
            TryDeleteDirectory(stagingDirectory);
            TryDeleteFile(temporaryManifestPath);
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

    private static void WritePdf(
        string targetPath,
        IReadOnlyList<string> pages,
        IProgress<BookProcessingProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (pages.Count == 0)
        {
            throw new InvalidDataException("At least one puzzle page is required for PDF export.");
        }

        using var document = new PdfDocument();
        for (var index = 0; index < pages.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourcePath = pages[index];
            using var image = XImage.FromFile(sourcePath);
            if (image.PixelWidth != PageWidth || image.PixelHeight != PageHeight)
            {
                throw new InvalidDataException($"PDF source page must be {PageWidth} x {PageHeight} pixels: {sourcePath}");
            }

            var page = document.AddPage();
            page.Width = XUnit.FromPoint(PageWidthPoints);
            page.Height = XUnit.FromPoint(PageHeightPoints);
            using var graphics = XGraphics.FromPdfPage(page);
            graphics.DrawImage(image, 0, 0, PageWidthPoints, PageHeightPoints);
            progress?.Report(new BookProcessingProgress("Assembling interior PDF", index + 1, pages.Count));
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

    private static IReadOnlyList<string> DiscoverOrderedPages(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .Where(path => SupportedImageExtensions.Contains(Path.GetExtension(path)))
            .OrderBy(Path.GetFileName, NaturalFileNameComparer.Instance)
            .ToArray();
    }

    private static async Task<string> CalculateSettingsSignatureAsync(
        string rootPath,
        string brandId,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var path in new[]
                 {
                     Path.Combine(rootPath, "settings.json"),
                     Path.Combine(rootPath, "brands", brandId, "settings.json")
                 })
        {
            hash.AppendData(await File.ReadAllBytesAsync(path, cancellationToken));
            hash.AppendData([0]);
        }

        return $"sha256:{Convert.ToHexStringLower(hash.GetHashAndReset())}";
    }

    private static async Task WriteManifestAsync(
        string path,
        OutputManifest manifest,
        CancellationToken cancellationToken)
    {
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static void PublishAtomically(
        string stagingDirectory,
        string outputDirectory,
        string temporaryManifestPath,
        string manifestPath)
    {
        var backupDirectory = $"{outputDirectory}.backup-{Guid.NewGuid():N}";
        var hadPreviousOutput = Directory.Exists(outputDirectory);
        if (hadPreviousOutput)
        {
            Directory.Move(outputDirectory, backupDirectory);
        }

        var published = false;
        try
        {
            Directory.Move(stagingDirectory, outputDirectory);
            File.Move(temporaryManifestPath, manifestPath, overwrite: true);
            published = true;
        }
        finally
        {
            if (!published)
            {
                if (Directory.Exists(outputDirectory) && !Directory.Exists(stagingDirectory))
                {
                    Directory.Move(outputDirectory, stagingDirectory);
                }

                if (hadPreviousOutput && Directory.Exists(backupDirectory) && !Directory.Exists(outputDirectory))
                {
                    Directory.Move(backupDirectory, outputDirectory);
                }
            }
        }

        TryDeleteDirectory(backupDirectory);
    }

    private static string ResolveChildDirectory(string parent, string child, string errorCode)
    {
        var fullParent = Path.GetFullPath(parent);
        var fullChild = Path.GetFullPath(Path.Combine(fullParent, child));
        var expectedPrefix = fullParent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!fullChild.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(fullChild))
        {
            throw new WordSearchGenerationException(errorCode, $"Directory was not found: {child}");
        }

        return fullChild;
    }

    private static string RequireValue(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }

    private static void ValidatePathSegment(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value is "." or ".." || value.Contains('/') || value.Contains('\\') || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new WordSearchGenerationException("path_invalid", $"{parameterName} must be a single safe path segment.");
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Unpublished staging/backup data can be cleaned by a later run.
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Unpublished temporary data can be cleaned by a later run.
        }
    }

    private sealed record PdfManifest(
        string RelativePath,
        long LengthBytes,
        int PageCount,
        int Width,
        int Height,
        int Dpi);

    private sealed record OutputManifest(
        int SchemaVersion,
        string BookId,
        string BrandId,
        string DataContentHash,
        string BrandFingerprint,
        string SettingsSignature,
        DateTimeOffset ProcessedAtUtc,
        int PuzzlePageCount,
        int FrontPageCount,
        int BackPageCount,
        PdfManifest Pdf,
        IReadOnlyList<BookAnswerOutput> Answers);

    internal sealed class NaturalFileNameComparer : IComparer<string?>
    {
        public static NaturalFileNameComparer Instance { get; } = new();

        public int Compare(string? left, string? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return -1;
            if (right is null) return 1;

            var leftIndex = 0;
            var rightIndex = 0;
            while (leftIndex < left.Length && rightIndex < right.Length)
            {
                if (char.IsDigit(left[leftIndex]) && char.IsDigit(right[rightIndex]))
                {
                    var leftStart = leftIndex;
                    var rightStart = rightIndex;
                    while (leftIndex < left.Length && char.IsDigit(left[leftIndex])) leftIndex++;
                    while (rightIndex < right.Length && char.IsDigit(right[rightIndex])) rightIndex++;
                    var leftNumber = left.AsSpan(leftStart, leftIndex - leftStart).TrimStart('0');
                    var rightNumber = right.AsSpan(rightStart, rightIndex - rightStart).TrimStart('0');
                    var lengthComparison = leftNumber.Length.CompareTo(rightNumber.Length);
                    if (lengthComparison != 0) return lengthComparison;
                    var numberComparison = leftNumber.CompareTo(rightNumber, StringComparison.Ordinal);
                    if (numberComparison != 0) return numberComparison;
                    continue;
                }

                var characterComparison = char.ToUpperInvariant(left[leftIndex]).CompareTo(char.ToUpperInvariant(right[rightIndex]));
                if (characterComparison != 0) return characterComparison;
                leftIndex++;
                rightIndex++;
            }

            var remainderComparison = (left.Length - leftIndex).CompareTo(right.Length - rightIndex);
            return remainderComparison != 0 ? remainderComparison : StringComparer.Ordinal.Compare(left, right);
        }
    }
}
