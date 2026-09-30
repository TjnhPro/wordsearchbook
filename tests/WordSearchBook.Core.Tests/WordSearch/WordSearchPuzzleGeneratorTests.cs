using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Core.WordSearch.Domain;
using WordSearchBook.Core.WordSearch.Generation;

namespace WordSearchBook.Core.Tests.WordSearch;

public sealed class WordSearchPuzzleGeneratorTests
{
    private static readonly string[] Keys =
    [
        "REDPANDA", "BALDEAGLE", "BLUEWHALE", "POLARBEAR", "SNOWLEOPARD",
        "SEATURTLE", "GIANTPANDA", "GRAYWOLF", "BROWNBEAR", "WILDHORSE",
        "KINGCOBRA", "TIGERSHARK", "GOLDENEAGLE", "BLACKSWAN", "RIVEROTTER",
        "MOUNTAINGOAT", "DESERTFOX", "GREENTURTLE", "WHITETIGER", "CORALSNAKE"
    ];

    [Fact]
    public void GeneratesPuzzleAndAnswerWithAPlacementForEveryKey()
    {
        var puzzle = new WordSearchPuzzleGenerator().Generate(Keys, new BoardSize(20, 20));

        Assert.Equal(20, puzzle.Data.GetLength(0));
        Assert.Equal(20, puzzle.Data.GetLength(1));
        Assert.Equal(Keys.Order(), puzzle.Placements.Select(item => item.WordSearchKey).Order());
        Assert.All(puzzle.Data.Cast<char>(), character => Assert.InRange(character, 'A', 'Z'));

        foreach (var placement in puzzle.Placements)
        {
            Assert.Equal(placement.WordSearchKey.Length, placement.Cells.Count);
            AssertStraightPlacement(placement);

            for (var index = 0; index < placement.Cells.Count; index++)
            {
                var cell = placement.Cells[index];
                Assert.InRange(cell.X, 0, 19);
                Assert.InRange(cell.Y, 0, 19);
                Assert.Equal(placement.WordSearchKey[index], puzzle.Data[cell.X, cell.Y]);
                Assert.Equal(placement.WordSearchKey[index], puzzle.Answer[cell.X, cell.Y]);
            }
        }
    }

    [Fact]
    public void RejectsWordsLongerThanBoard()
    {
        var exception = Assert.Throws<WordSearchGenerationException>(() =>
            new WordSearchPuzzleGenerator().Generate(["TOOLONG"], new BoardSize(4, 4)));

        Assert.Equal("word_too_long", exception.Code);
    }

    private static void AssertStraightPlacement(WordPlacement placement)
    {
        if (placement.Cells.Count < 2)
        {
            return;
        }

        var deltaX = placement.Cells[1].X - placement.Cells[0].X;
        var deltaY = placement.Cells[1].Y - placement.Cells[0].Y;
        Assert.InRange(deltaX, -1, 1);
        Assert.InRange(deltaY, -1, 1);
        Assert.NotEqual((0, 0), (deltaX, deltaY));

        for (var index = 2; index < placement.Cells.Count; index++)
        {
            Assert.Equal(deltaX, placement.Cells[index].X - placement.Cells[index - 1].X);
            Assert.Equal(deltaY, placement.Cells[index].Y - placement.Cells[index - 1].Y);
        }
    }
}
