using System.Security.Cryptography;
using System.Text.Json;
using WordSearchBook.Core.WordSearch.Application;
using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Core.WordSearch.Settings;
using WordSearchBook.Core.WordSearch.Validation;

namespace WordSearchBook.Infrastructure.WordSearch.Processing;

public sealed class BookProcessingService(
    IWordSearchBookGenerationService generationService,
    IBookDataValidationService dataValidationService,
    IBrandValidationService brandValidationService,
    IWordSearchSettingsReader settingsReader,
    IBookAnswerBatchExporter answerExporter,
    IBookQrPageRenderer qrPageRenderer,
    IBookInteriorPdfExporter pdfExporter,
    IBookOutputPublisher outputPublisher,
    IBookProcessingSessionGate sessionGate) : IBookProcessingService
{
    private const int ManifestSchemaVersion = 2;
    private const int PageWidth = 2588;
    private const int PageHeight = 3375;
    private const int OutputDpi = 300;
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
        var lease = await sessionGate.TryAcquireAsync($"{rootPath}|{request.BookId}", cancellationToken);
        if (lease is null)
        {
            throw new WordSearchGenerationException(
                "book_processing_already_running",
                $"Book '{request.BookId}' is already being processed.");
        }

        await using (lease)
        {
            return await ProcessCoreAsync();
        }

        async Task<BookProcessingResult> ProcessCoreAsync()
        {
            var dataPath = Path.Combine(bookDirectory, "data.csv");
            if (!File.Exists(dataPath))
            {
                throw new WordSearchGenerationException("input_not_found", $"CSV input was not found: {dataPath}");
            }

            var workspaceDirectory = Path.Combine(bookDirectory, ".workspace");
            var outputDirectory = Path.Combine(bookDirectory, "output");
            var answerDirectory = Path.Combine(outputDirectory, "answer");
            var pdfFileName = $"{request.BookId}.interior.pdf";
            var finalPdfPath = Path.Combine(outputDirectory, pdfFileName);
            var pendingPdfPath = Path.Combine(outputDirectory, $".{pdfFileName}.pending");
            var pdfWorkDirectory = Path.Combine(workspaceDirectory, "pdf-work");
            var manifestPath = Path.Combine(workspaceDirectory, "output.manifest.json");
            var pendingManifestPath = Path.Combine(workspaceDirectory, "output.manifest.pending.json");
            Directory.CreateDirectory(workspaceDirectory);
            CleanupPendingFiles(pendingPdfPath, pendingManifestPath, answerDirectory);

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
                var globalSettings = await settingsReader.ReadGlobalAsync(rootPath, cancellationToken);
                var dataState = await dataValidationService.CheckStateAsync(
                    rootPath,
                    request.BookId,
                    globalSettings.MaximumKeywordLength,
                    cancellationToken);
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

                var settings = await settingsReader.ReadAsync(rootPath, request.BrandId, cancellationToken);

                var settingsSignature = await CalculateSettingsSignatureAsync(rootPath, request.BrandId, cancellationToken);
                progress?.Report(new BookProcessingProgress("Generating topics", 0, dataState.TopicCount));
                var generation = await generationService.GenerateAsync(
                    new WordSearchGenerationRequest(rootPath, request.BookId, request.BrandId),
                    cancellationToken,
                    new GenerationProgress(progress));
                var cacheDirectory = Path.GetDirectoryName(generation.ManifestPath)!;
                Directory.CreateDirectory(outputDirectory);
                Directory.CreateDirectory(answerDirectory);
                var preparedAnswers = await answerExporter.PrepareAsync(
                    cacheDirectory,
                    generation.Topics,
                    outputDirectory,
                    globalSettings.MaximumProcessingConcurrency,
                    progress,
                    cancellationToken);

                var frontPages = DiscoverOrderedPages(Path.Combine(brandDirectory, "front"));
                var puzzlePages = generation.Topics
                    .OrderBy(topic => topic.Index)
                    .Select(topic => topic.Artifacts.Single(artifact => artifact.Kind == WordSearchArtifactKind.Page))
                    .Select(artifact => Path.Combine(cacheDirectory, artifact.RelativePath))
                    .ToArray();
                var backPages = DiscoverOrderedPages(Path.Combine(brandDirectory, "back"));
                var qrTemplatePath = Path.Combine(brandDirectory, BrandValidationDefinition.QrPageRelativePath);
                var qrCachePath = Path.Combine(cacheDirectory, "page-qr.png");
                IReadOnlyList<string> qrPages;
                if (File.Exists(qrTemplatePath))
                {
                    var qrSettings = settings.Brand.QrPage ?? throw new WordSearchGenerationException(
                        "qr_settings_required",
                        "QR Page settings are required while page_qr.png is present.");
                    progress?.Report(new BookProcessingProgress("Generating QR page"));
                    var qrPage = await qrPageRenderer.RenderAsync(
                        qrTemplatePath,
                        qrCachePath,
                        request.BookId,
                        qrSettings,
                        cancellationToken);
                    qrPages = [qrPage.Path];
                }
                else
                {
                    TryDeleteFile(qrCachePath);
                    qrPages = [];
                }

                var orderedPdfPages = frontPages
                    .Concat(puzzlePages)
                    .Concat(backPages)
                    .Concat(qrPages)
                    .ToArray();
                var preparedPdf = await pdfExporter.PrepareAsync(
                    orderedPdfPages,
                    pdfWorkDirectory,
                    pendingPdfPath,
                    finalPdfPath,
                    globalSettings.MaximumProcessingConcurrency,
                    progress,
                    cancellationToken);

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
                var answers = preparedAnswers.Select(answer => answer.Output).ToArray();
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
                    qrPages.Count,
                    new PdfManifest(
                        pdfFileName,
                        preparedPdf.LengthBytes,
                        preparedPdf.PageCount,
                        PageWidth,
                        PageHeight,
                        OutputDpi),
                    answers);
                await WriteManifestAsync(pendingManifestPath, manifest, cancellationToken);

                progress?.Report(new BookProcessingProgress("Publishing output"));
                await outputPublisher.PublishAsync(
                    new BookOutputPublicationRequest(
                        outputDirectory,
                        preparedPdf,
                        preparedAnswers,
                        pendingManifestPath,
                        manifestPath),
                    cancellationToken);

                return new BookProcessingResult(
                    request.BookId,
                    request.BrandId,
                    finalPdfPath,
                    answerDirectory,
                    generation.Topics.Count,
                    frontPages.Count,
                    backPages.Count,
                    qrPages.Count,
                    preparedPdf.PageCount,
                    preparedPdf.LengthBytes,
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
                CleanupPendingFiles(pendingPdfPath, pendingManifestPath, answerDirectory);
            }
        }
    }

    private static IReadOnlyList<string> DiscoverOrderedPages(string directory)
    {
        if (!Directory.Exists(directory)) return [];
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
        await using var stream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, cancellationToken);
        await stream.FlushAsync(cancellationToken);
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

    private static void CleanupPendingFiles(
        string pendingPdfPath,
        string pendingManifestPath,
        string answerDirectory)
    {
        TryDeleteFile(pendingPdfPath);
        TryDeleteFile(pendingManifestPath);
        if (!Directory.Exists(answerDirectory)) return;
        foreach (var pendingAnswer in Directory.EnumerateFiles(answerDirectory, ".*.pending", SearchOption.TopDirectoryOnly))
        {
            TryDeleteFile(pendingAnswer);
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
            // A later run retries the same stable pending path.
        }
    }

    private sealed class GenerationProgress(IProgress<BookProcessingProgress>? progress)
        : IProgress<WordSearchGenerationProgress>
    {
        public void Report(WordSearchGenerationProgress value) => progress?.Report(new BookProcessingProgress(
            "Generating topics",
            value.Completed,
            value.Total,
            $"Topic {value.TopicIndex}: {value.TopicName}"));
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
        int QrPageCount,
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
