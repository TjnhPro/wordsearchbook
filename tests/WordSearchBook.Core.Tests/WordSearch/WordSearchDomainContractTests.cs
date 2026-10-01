using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Core.WordSearch.Domain;

namespace WordSearchBook.Core.Tests.WordSearch;

public sealed class WordSearchDomainContractTests
{
    [Fact]
    public void KeepsInputDisplayValueSeparateFromSearchKey()
    {
        var entry = new WordSearchEntry(2, "Red Panda", "REDPANDA");
        var topic = new WordSearchTopic(1, "Amazing Animals", "Stay curious.", [entry]);

        Assert.Equal("Red Panda", topic.Entries[0].Keyword);
        Assert.Equal("REDPANDA", topic.Entries[0].WordSearchKey);
    }

    [Fact]
    public void DescribesBoardAndComposedPageArtifactKinds()
    {
        var kinds = Enum.GetValues<WordSearchArtifactKind>();

        Assert.Equal(4, kinds.Length);
        Assert.Contains(WordSearchArtifactKind.BoardGame, kinds);
        Assert.Contains(WordSearchArtifactKind.BoardGameAnswer, kinds);
        Assert.Contains(WordSearchArtifactKind.Page, kinds);
        Assert.Contains(WordSearchArtifactKind.PageAnswer, kinds);
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

    [Fact]
    public void ProvidesACompleteDefaultBrandLayout()
    {
        var settings = WordSearchSettingsDefaults.CreateBrand();

        Assert.Equal((1200, 100, TextAlignment.Center), (settings.Topic.X, settings.Topic.Y, settings.Topic.Alignment));
        Assert.Equal(new LayoutRectangle(300, 3000, 1988, 220), settings.Quote.Rectangle);
        Assert.Null(settings.QrPage);
        Assert.Equal(new LayoutRectangle(200, 400, 2000, 2000), settings.BoardGame.Rectangle);
        Assert.Equal(4, settings.KeywordList.Columns.Count);
        Assert.Equal(80, settings.KeywordList.StepY);
        Assert.Equal("Arial", settings.KeywordList.Font.Name);
        Assert.Equal(14, settings.PageNumber.Font.Size);
        Assert.Equal(new AnswerLineSettings(28, "#8B1E1E"), settings.AnswerLine);
    }
}
