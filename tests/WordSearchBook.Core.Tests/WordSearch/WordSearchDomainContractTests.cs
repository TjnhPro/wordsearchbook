using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Core.WordSearch.Domain;

namespace WordSearchBook.Core.Tests.WordSearch;

public sealed class WordSearchDomainContractTests
{
    [Fact]
    public void KeepsInputDisplayValueSeparateFromSearchKey()
    {
        var entry = new WordSearchEntry(2, "Red Panda", "REDPANDA");
        var topic = new WordSearchTopic(1, "Amazing Animals", [entry]);

        Assert.Equal("Red Panda", topic.Entries[0].Keyword);
        Assert.Equal("REDPANDA", topic.Entries[0].WordSearchKey);
    }

    [Fact]
    public void DescribesAllFiveArtifactKinds()
    {
        var kinds = Enum.GetValues<WordSearchArtifactKind>();

        Assert.Equal(5, kinds.Length);
        Assert.Contains(WordSearchArtifactKind.BoardGame, kinds);
        Assert.Contains(WordSearchArtifactKind.BoardGameAnswer, kinds);
    }

    [Fact]
    public void KeepsPuzzleDataAnswerAndPlacementTogether()
    {
        var puzzle = new WordSearchPuzzle(
            new[,] { { 'C', 'A', 'T' } },
            new[,] { { 'C', 'A', 'T' } },
            [new WordPlacement("CAT", [new GridPoint(0, 0), new GridPoint(0, 1), new GridPoint(0, 2)])]);

        Assert.Equal('A', puzzle.Data[0, 1]);
        Assert.Equal(puzzle.Data[0, 1], puzzle.Answer[0, 1]);
        Assert.Equal(3, puzzle.Placements[0].Cells.Count);
    }
}
