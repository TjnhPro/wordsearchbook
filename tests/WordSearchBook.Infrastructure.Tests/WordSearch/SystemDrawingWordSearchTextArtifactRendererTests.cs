using System.Drawing;
using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Core.WordSearch.Domain;
using WordSearchBook.Infrastructure.WordSearch.Input;
using WordSearchBook.Infrastructure.WordSearch.Rendering;
using WordSearchBook.Infrastructure.WordSearch.Settings;

namespace WordSearchBook.Infrastructure.Tests.WordSearch;

public sealed class SystemDrawingWordSearchTextArtifactRendererTests
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(4, 0, 400)]
    [InlineData(5, 100, 0)]
    [InlineData(19, 300, 400)]
    public void PlacesKeywordsDownColumns(int index, float expectedX, float expectedY)
    {
        var cell = SystemDrawingWordSearchTextArtifactRenderer.GetKeywordCell(index, 400, 500);

        Assert.Equal(expectedX, cell.X);
        Assert.Equal(expectedY, cell.Y);
        Assert.Equal(100, cell.Width);
        Assert.Equal(100, cell.Height);
    }

    [Fact]
    public void RendersTopicKeywordListAndPageNumber()
    {
        var renderer = new SystemDrawingWordSearchTextArtifactRenderer();
        var topic = renderer.RenderTopic("Amazing Animals", Style(1200, 200, 24));
        var keywords = renderer.RenderKeywordList(
            Enumerable.Range(1, 20).Select(index => $"Word {index}").ToArray(),
            Style(2000, 500, 12));
        var pageNumber = renderer.RenderPageNumber(1, Style(200, 100, 14));

        AssertArtifact(topic, WordSearchArtifactKind.Topic, 1200, 200);
        AssertArtifact(keywords, WordSearchArtifactKind.KeywordList, 2000, 500);
        AssertArtifact(pageNumber, WordSearchArtifactKind.PageNumber, 200, 100);
    }

    [Fact]
    public void NormalizesTopicAndKeywordsToUppercaseBeforeRendering()
    {
        var renderer = new SystemDrawingWordSearchTextArtifactRenderer();
        var mixedCaseTopic = renderer.RenderTopic("Amazing Animals", Style(1200, 200, 24));
        var uppercaseTopic = renderer.RenderTopic("AMAZING ANIMALS", Style(1200, 200, 24));
        var mixedCaseKeywords = Enumerable.Range(1, 20).Select(index => $"Word {index}").ToArray();
        var uppercaseKeywords = mixedCaseKeywords.Select(keyword => keyword.ToUpperInvariant()).ToArray();

        var mixedCaseList = renderer.RenderKeywordList(mixedCaseKeywords, Style(2000, 500, 12));
        var uppercaseList = renderer.RenderKeywordList(uppercaseKeywords, Style(2000, 500, 12));

        Assert.Equal(uppercaseTopic.Content, mixedCaseTopic.Content);
        Assert.Equal(uppercaseList.Content, mixedCaseList.Content);
    }

    [Fact]
    public void RejectsTextThatDoesNotFitConfiguredRectangle()
    {
        var exception = Assert.Throws<WordSearchGenerationException>(() =>
            new SystemDrawingWordSearchTextArtifactRenderer().RenderTopic(
                "This topic cannot fit",
                Style(10, 10, 24)));

        Assert.Equal("text_overflow", exception.Code);
    }

    [Fact]
    public async Task RendersAllThreeTextArtifactsFromFixture()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "SingleTopicBook");
        var settings = await new JsonWordSearchSettingsReader().ReadAsync(root, "demo");
        var topic = Assert.Single(await new CsvWordSearchInputReader().ReadAsync(
            Path.Combine(root, "input", "sample-book", "data.csv")));
        var renderer = new SystemDrawingWordSearchTextArtifactRenderer();

        var topicArtifact = renderer.RenderTopic(topic.Name, settings.Brand.Topic);
        var keywordArtifact = renderer.RenderKeywordList(
            topic.Entries.Select(entry => entry.Keyword).ToArray(),
            settings.Brand.KeywordList);
        var pageNumberArtifact = renderer.RenderPageNumber(topic.Index, settings.Brand.PageNumber);

        AssertArtifact(topicArtifact, WordSearchArtifactKind.Topic, 2000, 200);
        AssertArtifact(keywordArtifact, WordSearchArtifactKind.KeywordList, 2000, 400);
        AssertArtifact(pageNumberArtifact, WordSearchArtifactKind.PageNumber, 200, 80);
    }

    private static TextRegionSettings Style(int width, int height, float fontSize) =>
        new(new LayoutRectangle(0, 0, width, height), new FontSettings("Arial", fontSize, "#000000"));

    private static void AssertArtifact(
        RenderedWordSearchArtifact artifact,
        WordSearchArtifactKind expectedKind,
        int expectedWidth,
        int expectedHeight)
    {
        Assert.Equal(expectedKind, artifact.Kind);
        Assert.Equal(expectedWidth, artifact.Width);
        Assert.Equal(expectedHeight, artifact.Height);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, artifact.Content[..4]);

        using var stream = new MemoryStream(artifact.Content);
        using var bitmap = new Bitmap(stream);
        Assert.True(ContainsNonWhitePixel(bitmap));
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
