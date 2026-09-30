namespace WordSearchBook.Core.WordSearch.Domain;

public sealed record GridPoint(int X, int Y);

public sealed record WordPlacement(
    string WordSearchKey,
    IReadOnlyList<GridPoint> Cells);

public sealed record WordSearchPuzzle(
    char[,] Data,
    char[,] Answer,
    IReadOnlyList<WordPlacement> Placements);
