using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Infrastructure.WordSearch.Rendering;

namespace WordSearchBook.Infrastructure.Tests.WordSearch;

public sealed class QuoteLineLayoutTests
{
    [Fact]
    public void KeepsQuoteOnOneLineWhenItFits()
    {
        var lines = Split("Keep going.", maximumWidth: 20);

        Assert.Equal(["Keep going."], lines);
    }

    [Fact]
    public void GreedilyWrapsAtSpacesAndPreservesEveryWord()
    {
        var lines = Split("  One   two three  ", maximumWidth: 7);

        Assert.Equal(["One two", "three"], lines);
    }

    [Theory]
    [InlineData("Extraordinary", 5, 10)]
    [InlineData("One two three four", 7, 10)]
    [InlineData("One two three", 7, 1)]
    public void RejectsWordWidthThirdLineOrInsufficientHeight(
        string quote,
        float maximumWidth,
        float maximumHeight)
    {
        var exception = Assert.Throws<WordSearchGenerationException>(() =>
            QuoteLineLayout.Split(
                quote,
                maximumWidth,
                maximumHeight,
                lineHeight: 1,
                value => value.Length,
                "Quote does not fit."));

        Assert.Equal("quote_layout_overflow", exception.Code);
        Assert.Equal("Quote does not fit.", exception.Message);
    }

    private static IReadOnlyList<string> Split(string quote, float maximumWidth) =>
        QuoteLineLayout.Split(
            quote,
            maximumWidth,
            maximumHeight: 10,
            lineHeight: 1,
            value => value.Length,
            "Quote does not fit.");
}
