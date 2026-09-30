namespace WordSearchBook.Core.WordSearch.Domain;

public sealed record BoardSize(int Width, int Height);

public sealed record PageSize(int Width, int Height);

public sealed record LayoutRectangle(int X, int Y, int Width, int Height);

public sealed record FontSettings(string Name, float Size, string Color);

public sealed record TextRegionSettings(LayoutRectangle Rectangle, FontSettings Font);

public sealed record AnswerLineSettings(float Width, string Color);

public sealed record GlobalWordSearchSettings(BoardSize Board, PageSize Page);

public static class WordSearchSettingsDefaults
{
    public const int BoardWidth = 20;
    public const int BoardHeight = 20;
    public const int PageWidth = 2400;
    public const int PageHeight = 3000;

    public static GlobalWordSearchSettings CreateGlobal() => new(
        new BoardSize(BoardWidth, BoardHeight),
        new PageSize(PageWidth, PageHeight));

    public static BrandWordSearchSettings CreateBrand() => new(
        new TextRegionSettings(new LayoutRectangle(200, 100, 2000, 200), new FontSettings("Arial", 36, "#1A1A1A")),
        new TextRegionSettings(new LayoutRectangle(200, 400, 2000, 2000), new FontSettings("Arial", 18, "#000000")),
        new TextRegionSettings(new LayoutRectangle(200, 2450, 2000, 400), new FontSettings("Arial", 12, "#000000")),
        new TextRegionSettings(new LayoutRectangle(2100, 2900, 200, 80), new FontSettings("Arial", 14, "#000000")),
        new AnswerLineSettings(28, "#8B1E1E"));
}

public sealed record BrandWordSearchSettings(
    TextRegionSettings Topic,
    TextRegionSettings BoardGame,
    TextRegionSettings KeywordList,
    TextRegionSettings PageNumber,
    AnswerLineSettings AnswerLine);

public sealed record WordSearchSettingsBundle(
    GlobalWordSearchSettings Global,
    BrandWordSearchSettings Brand);
