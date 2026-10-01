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
    public void ReportsTopicOutsidePrintablePage()
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

            Assert.Equal("page_text_boundary_overflow", exception.Code);
            Assert.Equal(
                "Topic 'AMAZING ANIMALS' line 1 'AMAZING' is outside the printable page. Adjust the Topic position in Brand Settings.",
                exception.Message);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DrawsMultiWordTopicAcrossTwoConsecutiveLines()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "page_layout.png");
            WriteImage(path, 2588, 3375, Color.White);
            var settings = CreateSettings();
            var rendered = new SystemDrawingWordSearchPageRenderer().Render(
                path,
                CreateTopic("WIND DOWN STRETCH"),
                1,
                CreateBoardArtifact(WordSearchArtifactKind.BoardGame),
                settings,
                WordSearchArtifactKind.Page);

            using var stream = new MemoryStream(rendered.Content);
            using var page = new Bitmap(stream);
            using var graphics = Graphics.FromImage(page);
            using var font = new Font(
                settings.Brand.Topic.Font.Name,
                settings.Brand.Topic.Font.Size,
                FontStyle.Regular);
            var lineHeight = font.GetHeight(graphics);

            Assert.True(ContainsInk(page, settings.Brand.Topic.Y, settings.Brand.Topic.Y + lineHeight));
            Assert.True(ContainsInk(page, settings.Brand.Topic.Y + lineHeight, settings.Brand.Topic.Y + (2 * lineHeight)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ReportsSecondTopicLineOutsidePrintablePage()
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
                    Topic = settings.Brand.Topic with { Y = 3200 }
                }
            };

            var exception = Assert.Throws<WordSearchGenerationException>(() =>
                new SystemDrawingWordSearchPageRenderer().Render(
                    path,
                    CreateTopic("WIND DOWN STRETCH"),
                    1,
                    CreateBoardArtifact(WordSearchArtifactKind.BoardGame),
                    settings,
                    WordSearchArtifactKind.Page));

            Assert.Equal("page_text_boundary_overflow", exception.Code);
            Assert.Equal(
                "Topic 'WIND DOWN STRETCH' line 2 'DOWN STRETCH' is outside the printable page. Adjust the Topic position in Brand Settings.",
                exception.Message);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AllowsKeywordToCrossColumnMidpointWhenColumnEnvelopesDoNotTouch()
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
                    KeywordList = settings.Brand.KeywordList with
                    {
                        Columns =
                        [
                            new KeywordColumnAnchor(443, 2300),
                            new KeywordColumnAnchor(1010, 2300),
                            new KeywordColumnAnchor(1578, 2300),
                            new KeywordColumnAnchor(2146, 2300)
                        ],
                        Font = new FontSettings("Arial", 10, "#000000")
                    }
                }
            };
            var topic = CreateTopic(index => index == 5 ? "OFFER ENCOURAGEMENT" : "A");

            var rendered = new SystemDrawingWordSearchPageRenderer().Render(
                path,
                topic,
                85,
                CreateBoardArtifact(WordSearchArtifactKind.BoardGame),
                settings,
                WordSearchArtifactKind.Page);

            Assert.Equal(WordSearchArtifactKind.Page, rendered.Kind);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ReportsKeywordEnvelopesThatTouchAcrossAdjacentColumns()
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
                    KeywordList = settings.Brand.KeywordList with
                    {
                        Columns =
                        [
                            new KeywordColumnAnchor(300, 1000),
                            new KeywordColumnAnchor(350, 1000),
                            new KeywordColumnAnchor(1300, 1000),
                            new KeywordColumnAnchor(1800, 1000)
                        ]
                    }
                }
            };
            var topic = CreateTopic(index => index switch
            {
                4 => "LEFT ENVELOPE",
                7 => "RIGHT ENVELOPE",
                _ => "A"
            });

            var exception = Assert.Throws<WordSearchGenerationException>(() =>
                new SystemDrawingWordSearchPageRenderer().Render(
                    path,
                    topic,
                    1,
                    CreateBoardArtifact(WordSearchArtifactKind.BoardGame),
                    settings,
                    WordSearchArtifactKind.Page));

            Assert.Equal("keyword_slot_width_overflow", exception.Code);
            Assert.Contains("CSV row 6, topic 'AMAZING ANIMALS': Keyword 'LEFT ENVELOPE'", exception.Message, StringComparison.Ordinal);
            Assert.Contains("touches or overlaps Keyword 'RIGHT ENVELOPE' from CSV row 9", exception.Message, StringComparison.Ordinal);
            Assert.Contains("between keyword columns 1 and 2", exception.Message, StringComparison.Ordinal);
            Assert.Contains("Shorten either Keyword", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ReportsKeywordRowsThatTouchWithinAColumn()
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
                    KeywordList = settings.Brand.KeywordList with { StepY = 1 }
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

            Assert.Equal("keyword_slot_height_overflow", exception.Code);
            Assert.Contains("Keyword 'KEYWORD 01' touches or overlaps Keyword 'KEYWORD 02' from CSV row 3", exception.Message, StringComparison.Ordinal);
            Assert.Contains("in keyword column 1", exception.Message, StringComparison.Ordinal);
            Assert.Contains("Increase the keyword row spacing", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ReportsKeywordOutsidePrintablePageWithCsvContext()
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
                    KeywordList = settings.Brand.KeywordList with
                    {
                        Columns =
                        [
                            new KeywordColumnAnchor(0, 1000),
                            new KeywordColumnAnchor(800, 1000),
                            new KeywordColumnAnchor(1300, 1000),
                            new KeywordColumnAnchor(1800, 1000)
                        ]
                    }
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

            Assert.Equal("page_text_boundary_overflow", exception.Code);
            Assert.Contains("CSV row 2, topic 'AMAZING ANIMALS': Keyword 'KEYWORD 01'", exception.Message, StringComparison.Ordinal);
            Assert.Contains("outside the printable page", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ReportsPageNumberOutsidePrintablePage()
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
                    PageNumber = settings.Brand.PageNumber with { X = 0, Alignment = TextAlignment.Center }
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

            Assert.Equal("page_text_boundary_overflow", exception.Code);
            Assert.Equal(
                "Page number '1' is outside the printable page. Adjust the Page number position in Brand Settings.",
                exception.Message);
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

    private static WordSearchTopic CreateTopic(Func<int, string>? keywordFactory = null) => new(
        1,
        "AMAZING ANIMALS",
        Enumerable.Range(1, 20)
            .Select(index => new WordSearchEntry(
                index + 1,
                keywordFactory?.Invoke(index - 1) ?? $"KEYWORD {index:00}",
                $"KEYWORD{index:00}"))
            .ToArray());

    private static WordSearchTopic CreateTopic(string topicName) =>
        CreateTopic() with { Name = topicName };

    private static bool ContainsInk(Bitmap image, float top, float bottom)
    {
        var firstRow = Math.Max(0, (int)MathF.Floor(top));
        var lastRow = Math.Min(image.Height, (int)MathF.Ceiling(bottom));
        for (var y = firstRow; y < lastRow; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                if (image.GetPixel(x, y).ToArgb() != Color.White.ToArgb())
                {
                    return true;
                }
            }
        }

        return false;
    }

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
