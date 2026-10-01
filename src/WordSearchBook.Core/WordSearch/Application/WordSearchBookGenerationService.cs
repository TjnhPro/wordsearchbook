using WordSearchBook.Core.WordSearch.Caching;
using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Core.WordSearch.Input;
using WordSearchBook.Core.WordSearch.Settings;
using WordSearchBook.Core.WordSearch.Validation;

namespace WordSearchBook.Core.WordSearch.Application;

public sealed class WordSearchBookGenerationService(
    IWordSearchInputReader inputReader,
    IWordSearchSettingsReader settingsReader,
    IWordSearchTopicBatchProcessor topicBatchProcessor,
    IWordSearchCachePublisher cachePublisher,
    IBrandValidationService validationService) : IWordSearchBookGenerationService
{
    public async Task<WordSearchGenerationResult> GenerateAsync(
        WordSearchGenerationRequest request,
        CancellationToken cancellationToken = default,
        IProgress<WordSearchGenerationProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var rootPath = Path.GetFullPath(RequireValue(request.RootPath, nameof(request.RootPath)));
        if (!Directory.Exists(rootPath))
        {
            throw new WordSearchGenerationException("root_not_found", $"Root directory was not found: {rootPath}");
        }

        ValidatePathSegment(request.BookId, nameof(request.BookId));
        ValidatePathSegment(request.BrandId, nameof(request.BrandId));

        var brandValidation = await validationService.CheckStateAsync(rootPath, request.BrandId, cancellationToken);
        if (brandValidation.Status != BrandValidationStatus.Validated)
        {
            var reason = brandValidation.ReasonCode is null ? string.Empty : $" Reason: {brandValidation.ReasonCode}.";
            throw new WordSearchGenerationException(
                "brand_not_validated",
                $"Brand '{request.BrandId}' assets must be validated before generation.{reason}");
        }

        var normalizedRequest = request with { RootPath = rootPath };
        var settings = await settingsReader.ReadAsync(rootPath, request.BrandId, cancellationToken);
        var dataCsvPath = Path.Combine(rootPath, "input", request.BookId, "data.csv");
        var pageLayoutPath = Path.Combine(rootPath, "brands", request.BrandId, "page_layout.png");
        var topics = await inputReader.ReadAsync(dataCsvPath, settings.Global.MaximumKeywordLength, cancellationToken);
        await using var cacheSession = await cachePublisher.OpenAsync(normalizedRequest, settings, cancellationToken);
        var generatedTopics = await topicBatchProcessor.ProcessAsync(
            topics,
            pageLayoutPath,
            settings,
            cacheSession,
            progress,
            cancellationToken);
        return await cacheSession.CommitAsync(generatedTopics, cancellationToken);
    }

    private static string RequireValue(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }

    private static void ValidatePathSegment(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value is "." or ".." || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || value.Contains('/') || value.Contains('\\'))
        {
            throw new WordSearchGenerationException("path_invalid", $"{parameterName} must be a single safe path segment.");
        }
    }
}
