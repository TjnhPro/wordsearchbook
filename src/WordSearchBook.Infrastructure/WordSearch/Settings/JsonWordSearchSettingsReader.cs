using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Core.WordSearch.Domain;
using WordSearchBook.Core.WordSearch.Settings;

namespace WordSearchBook.Infrastructure.WordSearch.Settings;

public sealed partial class JsonWordSearchSettingsReader : IWordSearchSettingsReader
{
    private const int SupportedBoardWidth = WordSearchSettingsDefaults.BoardWidth;
    private const int SupportedBoardHeight = WordSearchSettingsDefaults.BoardHeight;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true
    };

    public async Task<WordSearchSettingsBundle> ReadAsync(
        string rootPath,
        string brandId,
        CancellationToken cancellationToken = default)
    {
        var global = await ReadGlobalAsync(rootPath, cancellationToken);
        var brand = await ReadBrandAsync(rootPath, brandId, global, cancellationToken);
        return new WordSearchSettingsBundle(global, brand);
    }

    public async Task<GlobalWordSearchSettings> ReadGlobalAsync(
        string rootPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        var path = Path.Combine(rootPath, "settings.json");
        await EnsureDefaultGlobalSettingsAsync(path, cancellationToken);
        var global = await ReadJsonAsync<GlobalWordSearchSettings>(path, cancellationToken);
        ValidateGlobal(global);
        return global;
    }

    public async Task<BrandWordSearchSettings> ReadBrandAsync(
        string rootPath,
        string brandId,
        GlobalWordSearchSettings global,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(global);
        ValidatePathSegment(brandId, nameof(brandId));
        var brand = await ReadJsonAsync<BrandWordSearchSettings>(
            Path.Combine(rootPath, "brands", brandId, "settings.json"),
            cancellationToken);
        ValidateBrand(global, brand);
        return brand;
    }

    private static async Task<T> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new WordSearchGenerationException("settings_not_found", $"Settings file was not found: {path}");
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
                ?? throw new WordSearchGenerationException("settings_invalid", $"Settings file is empty: {path}");
        }
        catch (WordSearchGenerationException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new WordSearchGenerationException("settings_invalid", $"Settings JSON is invalid at '{path}': {exception.Message}", exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new WordSearchGenerationException("settings_read_failed", $"Settings file could not be read: {path}", exception);
        }
    }

    private static async Task EnsureDefaultGlobalSettingsAsync(string path, CancellationToken cancellationToken)
    {
        if (File.Exists(path))
        {
            return;
        }

        var directory = Path.GetDirectoryName(path)!;
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(directory);
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    WordSearchSettingsDefaults.CreateGlobal(),
                    JsonOptions,
                    cancellationToken);
            }

            try
            {
                File.Move(temporaryPath, path);
            }
            catch (IOException) when (File.Exists(path))
            {
                // Another refresh created the same default file first.
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new WordSearchGenerationException(
                "settings_create_failed",
                $"Default settings file could not be created: {path}",
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

    internal static void ValidateGlobal(GlobalWordSearchSettings global)
    {
        if (global.Board is null || global.Page is null)
        {
            throw Invalid("Global settings require board and page objects.");
        }

        if (global.Board.Width != SupportedBoardWidth || global.Board.Height != SupportedBoardHeight)
        {
            throw new WordSearchGenerationException(
                "board_size_unsupported",
                $"MVP board size must be {SupportedBoardWidth}x{SupportedBoardHeight}.");
        }

        if (global.Page.Width <= 0 || global.Page.Height <= 0)
        {
            throw Invalid("Page width and height must be positive.");
        }

    }

    internal static void ValidateBrand(GlobalWordSearchSettings global, BrandWordSearchSettings brand)
    {
        ValidateRegion("topic", brand.Topic, global.Page);
        ValidateRegion("boardGame", brand.BoardGame, global.Page);
        ValidateRegion("keywordList", brand.KeywordList, global.Page);
        ValidateRegion("pageNumber", brand.PageNumber, global.Page);

        var boardRectangle = brand.BoardGame.Rectangle;
        if (boardRectangle.Width != boardRectangle.Height || boardRectangle.Width % SupportedBoardWidth != 0)
        {
            throw Invalid($"boardGame.rectangle must be square and divisible by {SupportedBoardWidth}.");
        }

        if (brand.AnswerLine is null || brand.AnswerLine.Width <= 0)
        {
            throw Invalid("answerLine.width must be positive.");
        }

        ValidateColor("answerLine.color", brand.AnswerLine.Color);
    }

    private static void ValidateRegion(string name, TextRegionSettings? region, PageSize page)
    {
        if (region?.Rectangle is null || region.Font is null)
        {
            throw Invalid($"{name} requires rectangle and font objects.");
        }

        var rectangle = region.Rectangle;
        if (rectangle.X < 0 || rectangle.Y < 0 || rectangle.Width <= 0 || rectangle.Height <= 0)
        {
            throw Invalid($"{name}.rectangle must have non-negative coordinates and positive dimensions.");
        }

        if ((long)rectangle.X + rectangle.Width > page.Width || (long)rectangle.Y + rectangle.Height > page.Height)
        {
            throw Invalid($"{name}.rectangle must stay inside the configured page.");
        }

        if (string.IsNullOrWhiteSpace(region.Font.Name) || region.Font.Size <= 0)
        {
            throw Invalid($"{name}.font requires a name and positive size.");
        }

        ValidateColor($"{name}.font.color", region.Font.Color);
    }

    private static void ValidateColor(string fieldName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !HexColorPattern().IsMatch(value))
        {
            throw Invalid($"{fieldName} must use #RRGGBB or #AARRGGBB format.");
        }
    }

    private static void ValidatePathSegment(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value is "." or ".." || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || value.Contains('/') || value.Contains('\\'))
        {
            throw new ArgumentException("Value must be a single safe path segment.", parameterName);
        }
    }

    private static WordSearchGenerationException Invalid(string message) => new("settings_invalid", message);

    [GeneratedRegex("^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$")]
    private static partial Regex HexColorPattern();
}
