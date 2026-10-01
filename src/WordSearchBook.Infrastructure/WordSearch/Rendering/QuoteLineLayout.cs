using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Core.WordSearch.Domain;

namespace WordSearchBook.Infrastructure.WordSearch.Rendering;

internal static class QuoteLineLayout
{
    internal static IReadOnlyList<string> Split(
        string quote,
        float maximumWidth,
        float maximumHeight,
        float lineHeight,
        Func<string, float> measureWidth,
        string overflowMessage)
    {
        var normalized = QuoteText.Normalize(quote);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalized);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maximumWidth, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maximumHeight, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lineHeight, 0);
        ArgumentNullException.ThrowIfNull(measureWidth);

        if (measureWidth(normalized) <= maximumWidth)
        {
            EnsureHeight(lineHeight, maximumHeight, overflowMessage);
            return [normalized];
        }

        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Any(word => measureWidth(word) > maximumWidth))
        {
            throw Overflow(overflowMessage);
        }

        var firstLineWordCount = 1;
        while (firstLineWordCount < words.Length)
        {
            var candidate = string.Join(' ', words[..(firstLineWordCount + 1)]);
            if (measureWidth(candidate) > maximumWidth)
            {
                break;
            }

            firstLineWordCount++;
        }

        if (firstLineWordCount >= words.Length)
        {
            throw Overflow(overflowMessage);
        }

        var firstLine = string.Join(' ', words[..firstLineWordCount]);
        var secondLine = string.Join(' ', words[firstLineWordCount..]);
        if (measureWidth(secondLine) > maximumWidth)
        {
            throw Overflow(overflowMessage);
        }

        EnsureHeight(lineHeight * 2, maximumHeight, overflowMessage);
        return [firstLine, secondLine];
    }

    private static void EnsureHeight(float contentHeight, float maximumHeight, string overflowMessage)
    {
        if (contentHeight > maximumHeight)
        {
            throw Overflow(overflowMessage);
        }
    }

    private static WordSearchGenerationException Overflow(string message) =>
        new("quote_layout_overflow", message);
}
