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
    private const int SupportedPageWidth = WordSearchSettingsDefaults.PageWidth;
    private const int SupportedPageHeight = WordSearchSettingsDefaults.PageHeight;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<WordSearchSettingsBundle> ReadAsync(
        string rootPath,
        string brandId,
        CancellationToken cancellationToken = default)
    {
        var global = await ReadGlobalAsync(rootPath, cancellationToken);
        var brand = await ReadBrandAsync(rootPath, brandId, global, cancellationToken);
        if (brand.RequiresSave)
        {
            throw new WordSearchGenerationException(
                "brand_settings_update_required",
                $"Brand '{brandId}' settings must be saved to add Quote settings before validation, preview, or processing.");
        }

        return new WordSearchSettingsBundle(global, brand.Settings);
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

    public async Task<BrandSettingsReadResult> ReadBrandAsync(
        string rootPath,
        string brandId,
        GlobalWordSearchSettings global,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(global);
        ValidatePathSegment(brandId, nameof(brandId));
        var document = await ReadJsonAsync<BrandSettingsDocument>(
            Path.Combine(rootPath, "brands", brandId, "settings.json"),
            cancellationToken);
        var requiresSave = document.Quote is null;
        var brand = MapBrand(document, requiresSave);
        ValidateBrand(global, brand);
        return new BrandSettingsReadResult(brand, requiresSave);
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

        if (global.Page.Width != SupportedPageWidth || global.Page.Height != SupportedPageHeight)
        {
            throw new WordSearchGenerationException(
                "page_size_unsupported",
                $"Page size must be {SupportedPageWidth}x{SupportedPageHeight}.");
        }

        if (global.MaximumKeywordLength is < 1 or > 100)
        {
            throw new WordSearchGenerationException(
                "maximum_keyword_length_invalid",
                "Maximum keyword length must be between 1 and 100 characters.");
        }

        if (global.MaximumProcessingConcurrency is < 1 or > WordSearchSettingsDefaults.MaximumSupportedProcessingConcurrency)
        {
            throw new WordSearchGenerationException(
                "maximum_processing_concurrency_invalid",
                $"Maximum processing concurrency must be between 1 and {WordSearchSettingsDefaults.MaximumSupportedProcessingConcurrency}.");
        }
    }

    internal static void ValidateBrand(GlobalWordSearchSettings global, BrandWordSearchSettings brand)
    {
        ArgumentNullException.ThrowIfNull(brand);
        ValidateAnchor("topic", brand.Topic, global.Page);
        ValidateRegion("quote", brand.Quote, global.Page);
        ValidateRegion("boardGame", brand.BoardGame, global.Page);
        ValidateKeywordList(brand.KeywordList, global.Page);
        ValidateAnchor("pageNumber", brand.PageNumber, global.Page);

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

    private static BrandWordSearchSettings MapBrand(BrandSettingsDocument document, bool requiresSave)
    {
        if (document.Topic.ValueKind == JsonValueKind.Undefined ||
            document.KeywordList.ValueKind == JsonValueKind.Undefined ||
            document.PageNumber.ValueKind == JsonValueKind.Undefined ||
            document.BoardGame is null ||
            document.AnswerLine is null)
        {
            throw Invalid("Brand settings require topic, boardGame, keywordList, pageNumber and answerLine objects.");
        }

        return new BrandWordSearchSettings(
            ReadAnchor(document.Topic, "topic", TextAlignment.Center),
            requiresSave ? WordSearchSettingsDefaults.CreateBrand().Quote : document.Quote!,
            document.BoardGame,
            ReadKeywordList(document.KeywordList),
            ReadAnchor(document.PageNumber, "pageNumber", TextAlignment.Left),
            document.AnswerLine);
    }

    private static AnchoredTextSettings ReadAnchor(JsonElement element, string name, TextAlignment defaultAlignment)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Invalid($"{name} must be an object.");
        }

        if (element.TryGetProperty("rectangle", out _))
        {
            var legacy = element.Deserialize<LegacyTextRegionDocument>(JsonOptions)
                ?? throw Invalid($"{name} is invalid.");
            if (legacy.Rectangle is null || legacy.Font is null)
            {
                throw Invalid($"{name} requires rectangle and font objects.");
            }

            var x = defaultAlignment switch
            {
                TextAlignment.Center => legacy.Rectangle.X + (legacy.Rectangle.Width / 2),
                TextAlignment.Right => legacy.Rectangle.X + legacy.Rectangle.Width,
                _ => legacy.Rectangle.X
            };
            return new AnchoredTextSettings(x, legacy.Rectangle.Y, legacy.Font, defaultAlignment);
        }

        var current = element.Deserialize<AnchoredTextDocument>(JsonOptions)
            ?? throw Invalid($"{name} is invalid.");
        return new AnchoredTextSettings(
            current.X,
            current.Y,
            current.Font ?? throw Invalid($"{name}.font is required."),
            current.Alignment ?? defaultAlignment);
    }

    private static KeywordListSettings ReadKeywordList(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("keywordList must be an object.");
        }

        if (element.TryGetProperty("rectangle", out _))
        {
            var legacy = element.Deserialize<LegacyTextRegionDocument>(JsonOptions)
                ?? throw Invalid("keywordList is invalid.");
            if (legacy.Rectangle is null || legacy.Font is null)
            {
                throw Invalid("keywordList requires rectangle and font objects.");
            }

            var columnWidth = legacy.Rectangle.Width / 4;
            var columns = Enumerable.Range(0, 4)
                .Select(index => new KeywordColumnAnchor(
                    legacy.Rectangle.X + (index * columnWidth) + (columnWidth / 2),
                    legacy.Rectangle.Y))
                .ToArray();
            return new KeywordListSettings(
                columns,
                legacy.Rectangle.Height / 5,
                legacy.Font,
                TextAlignment.Center);
        }

        var current = element.Deserialize<KeywordListDocument>(JsonOptions)
            ?? throw Invalid("keywordList is invalid.");
        return new KeywordListSettings(
            current.Columns ?? [],
            current.StepY,
            current.Font ?? throw Invalid("keywordList.font is required."),
            current.Alignment ?? TextAlignment.Center);
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

    private static void ValidateAnchor(string name, AnchoredTextSettings? region, PageSize page)
    {
        if (region?.Font is null)
        {
            throw Invalid($"{name} requires a font object.");
        }

        if (region.X < 0 || region.X > page.Width || region.Y < 0 || region.Y > page.Height)
        {
            throw Invalid($"{name} anchor must stay inside the configured page.");
        }

        ValidateFont(name, region.Font);
        ValidateAlignment(name, region.Alignment);
    }

    private static void ValidateKeywordList(KeywordListSettings? region, PageSize page)
    {
        if (region?.Font is null || region.Columns is null)
        {
            throw Invalid("keywordList requires columns and font objects.");
        }

        if (region.Columns.Count != 4)
        {
            throw Invalid("keywordList.columns must contain exactly four anchors.");
        }

        if (region.StepY <= 0)
        {
            throw Invalid("keywordList.stepY must be positive.");
        }

        foreach (var column in region.Columns)
        {
            if (column is null || column.X < 0 || column.X > page.Width || column.Y < 0 || column.Y > page.Height)
            {
                throw Invalid("Every keywordList column anchor must stay inside the configured page.");
            }
        }

        ValidateFont("keywordList", region.Font);
        ValidateAlignment("keywordList", region.Alignment);
    }

    private static void ValidateFont(string name, FontSettings font)
    {
        if (string.IsNullOrWhiteSpace(font.Name) || font.Size <= 0)
        {
            throw Invalid($"{name}.font requires a name and positive size.");
        }

        ValidateColor($"{name}.font.color", font.Color);
    }

    private static void ValidateAlignment(string name, TextAlignment alignment)
    {
        if (!Enum.IsDefined(alignment))
        {
            throw Invalid($"{name}.alignment is invalid.");
        }
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

    private sealed record BrandSettingsDocument(
        JsonElement Topic,
        TextRegionSettings? Quote,
        TextRegionSettings? BoardGame,
        JsonElement KeywordList,
        JsonElement PageNumber,
        AnswerLineSettings? AnswerLine);

    private sealed record LegacyTextRegionDocument(LayoutRectangle? Rectangle, FontSettings? Font);

    private sealed record AnchoredTextDocument(int X, int Y, FontSettings? Font, TextAlignment? Alignment);

    private sealed record KeywordListDocument(
        IReadOnlyList<KeywordColumnAnchor>? Columns,
        int StepY,
        FontSettings? Font,
        TextAlignment? Alignment);

    [GeneratedRegex("^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$")]
    private static partial Regex HexColorPattern();
}
