using System.Drawing;
using System.Drawing.Imaging;
using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Core.WordSearch.Domain;
using WordSearchBook.Infrastructure.WordSearch.Rendering;

namespace WordSearchBook.Infrastructure.Tests.WordSearch;

public sealed class SystemDrawingWordSearchPageRendererTests
{
    [Fact]
    public void ComposesExactPageAndPlacesBoardAtConfiguredRectangle()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var layoutPath = Path.Combine(directory, "page_layout.png");
            WriteImage(layoutPath, 2588, 3375, Color.White);
            var renderer = new SystemDrawingWordSearchPageRenderer();

            var rendered = renderer.Render(
                layoutPath,
                CreateTopic(),
                1,
                CreateBoardArtifact(WordSearchArtifactKind.BoardGame),
                CreateSettings(),
                WordSearchArtifactKind.Page);

            Assert.Equal(WordSearchArtifactKind.Page, rendered.Kind);
            Assert.Equal(2588, rendered.Width);
            Assert.Equal(3375, rendered.Height);
            using var stream = new MemoryStream(rendered.Content);
            using var page = new Bitmap(stream);
            Assert.Equal(Color.Red.ToArgb(), page.GetPixel(110, 310).ToArgb());
            Assert.Equal(Color.White.ToArgb(), page.GetPixel(90, 290).ToArgb());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RejectsMissingPageLayoutWithStableCode()
    {
        var exception = Assert.Throws<WordSearchGenerationException>(() =>
            new SystemDrawingWordSearchPageRenderer().Render(
                Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.png"),
                CreateTopic(),
                1,
                CreateBoardArtifact(WordSearchArtifactKind.BoardGame),
                CreateSettings(),
                WordSearchArtifactKind.Page));

        Assert.Equal("page_layout_not_found", exception.Code);
    }

    [Fact]
    public void RejectsWrongPageLayoutDimensionsWithStableCode()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "page_layout.png");
            WriteImage(path, 100, 100, Color.White);

            var exception = Assert.Throws<WordSearchGenerationException>(() =>
                new SystemDrawingWordSearchPageRenderer().Render(
                    path,
                    CreateTopic(),
                    1,
                    CreateBoardArtifact(WordSearchArtifactKind.BoardGame),
                    CreateSettings(),
                    WordSearchArtifactKind.Page));

            Assert.Equal("page_layout_invalid", exception.Code);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RejectsTextThatCrossesPageBoundary()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "page_layout.png");
            WriteImage(path, 2588, 3375, Color.White);
            var settings = CreateSettings();
            settings = settings with
            {
                Brand = settings.Brand with
                {
                    Topic = settings.Brand.Topic with { X = 0, Alignment = TextAlignment.Center }
                }
            };

            var exception = Assert.Throws<WordSearchGenerationException>(() =>
                new SystemDrawingWordSearchPageRenderer().Render(
                    path,
                    CreateTopic(),
                    1,
                    CreateBoardArtifact(WordSearchArtifactKind.BoardGame),
                    settings,
                    WordSearchArtifactKind.Page));

            Assert.Equal("page_text_overflow", exception.Code);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static WordSearchSettingsBundle CreateSettings()
    {
        var brand = new BrandWordSearchSettings(
            new AnchoredTextSettings(100, 100, new FontSettings("Arial", 20, "#000000"), TextAlignment.Left),
            new TextRegionSettings(new LayoutRectangle(100, 300, 400, 400), new FontSettings("Arial", 12, "#000000")),
            new KeywordListSettings(
                [
                    new KeywordColumnAnchor(300, 1000),
                    new KeywordColumnAnchor(800, 1000),
                    new KeywordColumnAnchor(1300, 1000),
                    new KeywordColumnAnchor(1800, 1000)
                ],
                100,
                new FontSettings("Arial", 12, "#000000"),
                TextAlignment.Center),
            new AnchoredTextSettings(100, 1600, new FontSettings("Arial", 14, "#000000"), TextAlignment.Left),
            new AnswerLineSettings(5, "#FF0000"));
        return new WordSearchSettingsBundle(WordSearchSettingsDefaults.CreateGlobal(), brand);
    }

    private static WordSearchTopic CreateTopic() => new(
        1,
        "AMAZING ANIMALS",
        Enumerable.Range(1, 20)
            .Select(index => new WordSearchEntry(index + 1, $"KEYWORD {index:00}", $"KEYWORD{index:00}"))
            .ToArray());

    private static RenderedWordSearchArtifact CreateBoardArtifact(WordSearchArtifactKind kind)
    {
        using var board = new Bitmap(400, 400, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(board))
        {
            graphics.Clear(Color.Red);
        }

        using var output = new MemoryStream();
        board.Save(output, ImageFormat.Png);
        return new RenderedWordSearchArtifact(kind, output.ToArray(), 400, 400);
    }

    private static void WriteImage(string path, int width, int height, Color color)
    {
        using var image = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(image))
        {
            graphics.Clear(color);
        }

        image.Save(path, ImageFormat.Png);
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"word-search-page-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
