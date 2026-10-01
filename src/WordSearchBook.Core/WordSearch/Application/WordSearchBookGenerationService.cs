using WordSearchBook.Core.WordSearch.Caching;
using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Core.WordSearch.Generation;
using WordSearchBook.Core.WordSearch.Input;
using WordSearchBook.Core.WordSearch.Rendering;
using WordSearchBook.Core.WordSearch.Settings;
using WordSearchBook.Core.WordSearch.Validation;

namespace WordSearchBook.Core.WordSearch.Application;

public sealed class WordSearchBookGenerationService(
    IWordSearchInputReader inputReader,
    IWordSearchSettingsReader settingsReader,
    IWordSearchPuzzleGenerator puzzleGenerator,
    IWordSearchBoardRenderer boardRenderer,
    IWordSearchPageRenderer pageRenderer,
    IWordSearchCachePublisher cachePublisher,
    IBrandValidationService validationService) : IWordSearchBookGenerationService
{
    public async Task<WordSearchGenerationResult> GenerateAsync(
        WordSearchGenerationRequest request,
        CancellationToken cancellationToken = default)
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
        var generatedTopics = new List<WordSearchTopicArtifactSet>(topics.Count);

        foreach (var topic in topics)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var puzzle = puzzleGenerator.Generate(
                topic.Entries.Select(entry => entry.WordSearchKey).ToArray(),
                settings.Global.Board);
            var board = boardRenderer.RenderData(puzzle, settings.Global.Board, settings.Brand.BoardGame);
            var answerBoard = boardRenderer.RenderAnswer(
                puzzle,
                settings.Global.Board,
                settings.Brand.BoardGame,
                settings.Brand.AnswerLine);
            var artifacts = new RenderedWordSearchArtifact[]
            {
                board,
                answerBoard,
                pageRenderer.Render(pageLayoutPath, topic, topic.Index, board, settings, WordSearchArtifactKind.Page),
                pageRenderer.Render(pageLayoutPath, topic, topic.Index, answerBoard, settings, WordSearchArtifactKind.PageAnswer)
            };

            generatedTopics.Add(new WordSearchTopicArtifactSet(topic, artifacts, puzzle.Placements));
        }

        return await cachePublisher.PublishAsync(normalizedRequest, settings, generatedTopics, cancellationToken);
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
