namespace WordSearchBook.Core.WordSearch.Domain;

public sealed record BoardSize(int Width, int Height);

public sealed record PageSize(int Width, int Height);

public sealed record LayoutRectangle(int X, int Y, int Width, int Height);

public sealed record FontSettings(string Name, float Size, string Color);

public sealed record TextRegionSettings(LayoutRectangle Rectangle, FontSettings Font);

public enum TextAlignment
{
    Left,
    Center,
    Right
}

public sealed record AnchoredTextSettings(
    int X,
    int Y,
    FontSettings Font,
    TextAlignment Alignment);

public sealed record KeywordColumnAnchor(int X, int Y);

public sealed record KeywordListSettings(
    IReadOnlyList<KeywordColumnAnchor> Columns,
    int StepY,
    FontSettings Font,
    TextAlignment Alignment);

public sealed record AnswerLineSettings(float Width, string Color);

public sealed record QrPageSettings(
    int X,
    int Y,
    int Size,
    string DomainName);

public sealed record GlobalWordSearchSettings(
    BoardSize Board,
    PageSize Page,
    int MaximumKeywordLength = WordSearchSettingsDefaults.MaximumKeywordLength,
    int MaximumProcessingConcurrency = WordSearchSettingsDefaults.MaximumProcessingConcurrency);

public static class WordSearchSettingsDefaults
{
    public const int BoardWidth = 20;
    public const int BoardHeight = 20;
    public const int PageWidth = 2588;
    public const int PageHeight = 3375;
    public const int MaximumKeywordLength = 13;
    public const int MaximumProcessingConcurrency = 4;
    public const int MaximumSupportedProcessingConcurrency = 12;

    public static GlobalWordSearchSettings CreateGlobal() => new(
        new BoardSize(BoardWidth, BoardHeight),
        new PageSize(PageWidth, PageHeight),
        MaximumKeywordLength,
        MaximumProcessingConcurrency);

    public static BrandWordSearchSettings CreateBrand() => new(
        new AnchoredTextSettings(1200, 100, new FontSettings("Arial", 36, "#1A1A1A"), TextAlignment.Center),
        new TextRegionSettings(new LayoutRectangle(300, 3000, 1988, 220), new FontSettings("Arial", 20, "#1A1A1A")),
        new TextRegionSettings(new LayoutRectangle(200, 400, 2000, 2000), new FontSettings("Arial", 18, "#000000")),
        new KeywordListSettings(
            [
                new KeywordColumnAnchor(450, 2450),
                new KeywordColumnAnchor(950, 2450),
                new KeywordColumnAnchor(1450, 2450),
                new KeywordColumnAnchor(1950, 2450)
            ],
            80,
            new FontSettings("Arial", 12, "#000000"),
            TextAlignment.Center),
        new AnchoredTextSettings(2100, 2900, new FontSettings("Arial", 14, "#000000"), TextAlignment.Left),
        new AnswerLineSettings(28, "#8B1E1E"));
}

public sealed record BrandWordSearchSettings(
    AnchoredTextSettings Topic,
    TextRegionSettings Quote,
    TextRegionSettings BoardGame,
    KeywordListSettings KeywordList,
    AnchoredTextSettings PageNumber,
    AnswerLineSettings AnswerLine,
    QrPageSettings? QrPage = null);

public sealed record WordSearchSettingsBundle(
    GlobalWordSearchSettings Global,
    BrandWordSearchSettings Brand);
