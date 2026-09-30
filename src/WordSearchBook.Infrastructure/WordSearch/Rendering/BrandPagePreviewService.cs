using WordSearchBook.Core.WordSearch.Application;
using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Core.WordSearch.Generation;
using WordSearchBook.Core.WordSearch.Rendering;
using WordSearchBook.Core.WordSearch.Settings;
using WordSearchBook.Infrastructure.WordSearch.Validation;

namespace WordSearchBook.Infrastructure.WordSearch.Rendering;

public sealed class BrandPagePreviewService(
    IWordSearchSettingsReader settingsReader,
    IWordSearchPuzzleGenerator puzzleGenerator,
    IWordSearchBoardRenderer boardRenderer,
    IWordSearchPageRenderer pageRenderer) : IBrandPagePreviewService
{
    public async Task<BrandPagePreviewResult> DrawAsync(
        string rootPath,
        string brandId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        if (!Directory.Exists(rootPath))
        {
            throw new WordSearchGenerationException("root_not_found", $"Root directory was not found: {rootPath}");
        }

        var certificatePath = JsonBrandValidationStateStore.ResolvePath(rootPath, brandId);
        var brandDirectory = Path.GetDirectoryName(certificatePath)!;
        var layoutPath = Path.Combine(brandDirectory, "page_layout.png");
        var outputPath = Path.Combine(brandDirectory, BrandPagePreviewSample.OutputFileName);
        var settings = await settingsReader.ReadAsync(rootPath, brandId, cancellationToken);
        var topic = BrandPagePreviewSample.CreateTopic();
        var puzzle = puzzleGenerator.Generate(
            topic.Entries.Select(entry => entry.WordSearchKey).ToArray(),
            settings.Global.Board);
        var board = boardRenderer.RenderData(puzzle, settings.Global.Board, settings.Brand.BoardGame);
        var page = pageRenderer.Render(
            layoutPath,
            topic,
            BrandPagePreviewSample.PageNumber,
            board,
            settings,
            WordSearchArtifactKind.Page);

        cancellationToken.ThrowIfCancellationRequested();
        await WriteAtomicallyAsync(outputPath, page.Content, cancellationToken);
        return new BrandPagePreviewResult(
            brandId,
            BrandPagePreviewSample.OutputFileName,
            page.Width,
            page.Height,
            DateTimeOffset.UtcNow);
    }

    private static async Task WriteAtomicallyAsync(
        string outputPath,
        byte[] content,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(outputPath)!;
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true))
            {
                await stream.WriteAsync(content, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, outputPath, overwrite: true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new WordSearchGenerationException(
                "brand_preview_write_failed",
                $"Brand page preview could not be written: {outputPath}",
                exception);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
