using System.Drawing;
using WordSearchBook.Core.WordSearch.Domain;
using WordSearchBook.Core.WordSearch.Generation;
using WordSearchBook.Infrastructure.WordSearch.Input;
using WordSearchBook.Infrastructure.WordSearch.Rendering;
using WordSearchBook.Infrastructure.WordSearch.Settings;

namespace WordSearchBook.Infrastructure.Tests.WordSearch;

public sealed class SystemDrawingWordSearchBoardRendererTests
{
    [Fact]
    public void RendersDataAndAnswerWithExpectedDimensionsAndAnswerLine()
    {
        var renderer = new SystemDrawingWordSearchBoardRenderer();
        var puzzle = CreateFixedPuzzle();
        var boardSize = new BoardSize(2, 2);
        var style = new TextRegionSettings(
            new LayoutRectangle(0, 0, 200, 200),
            new FontSettings("Arial", 18, "#000000"));

        var data = renderer.RenderData(puzzle, boardSize, style);
        var answer = renderer.RenderAnswer(puzzle, boardSize, style, new AnswerLineSettings(20, "#8B1E1E"));

        using var dataStream = new MemoryStream(data.Content);
        using var answerStream = new MemoryStream(answer.Content);
        using var dataBitmap = new Bitmap(dataStream);
        using var answerBitmap = new Bitmap(answerStream);
        Assert.Equal((200, 200), (dataBitmap.Width, dataBitmap.Height));
        Assert.Equal((200, 200), (answerBitmap.Width, answerBitmap.Height));
        Assert.InRange(dataBitmap.HorizontalResolution, 299f, 301f);
        Assert.True(ContainsNonWhitePixel(dataBitmap));

        var answerLinePixel = answerBitmap.GetPixel(100, 50);
        Assert.Equal(Color.FromArgb(139, 30, 30).ToArgb(), answerLinePixel.ToArgb());
        Assert.False(data.Content.SequenceEqual(answer.Content));
    }

    [Fact]
    public async Task RendersFixtureCsvIntoPuzzleAndAnswerPngs()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "SingleTopicBook");
        var settings = await new JsonWordSearchSettingsReader().ReadAsync(root, "demo");
        var topics = await new CsvWordSearchInputReader().ReadAsync(
            Path.Combine(root, "input", "sample-book", "data.csv"));
        var topic = Assert.Single(topics);
        var puzzle = new WordSearchPuzzleGenerator().Generate(
            topic.Entries.Select(entry => entry.WordSearchKey).ToArray(),
            settings.Global.Board);
        var renderer = new SystemDrawingWordSearchBoardRenderer();

        var data = renderer.RenderData(puzzle, settings.Global.Board, settings.Brand.BoardGame);
        var answer = renderer.RenderAnswer(
            puzzle,
            settings.Global.Board,
            settings.Brand.BoardGame,
            settings.Brand.AnswerLine);

        Assert.Equal(2000, data.Width);
        Assert.Equal(2000, data.Height);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, data.Content[..4]);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, answer.Content[..4]);
        Assert.Equal(20, puzzle.Placements.Count);
    }

    private static WordSearchPuzzle CreateFixedPuzzle()
    {
        var data = new[,] { { 'C', 'T' }, { 'A', 'S' } };
        var answer = new char[2, 2];
        answer[0, 0] = 'C';
        answer[1, 0] = 'A';
        return new WordSearchPuzzle(
            data,
            answer,
            [new WordPlacement("CA", [new GridPoint(0, 0), new GridPoint(1, 0)])]);
    }

    private static bool ContainsNonWhitePixel(Bitmap bitmap)
    {
        for (var x = 0; x < bitmap.Width; x++)
        {
            for (var y = 0; y < bitmap.Height; y++)
            {
                if (bitmap.GetPixel(x, y).ToArgb() != Color.White.ToArgb())
                {
                    return true;
                }
            }
        }

        return false;
    }
}
