using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Core.WordSearch.Domain;
using WordSearchBook.Core.WordSearch.Rendering;

namespace WordSearchBook.Infrastructure.WordSearch.Rendering;

public sealed class SystemDrawingWordSearchTextArtifactRenderer : IWordSearchTextArtifactRenderer
{
    private const int KeywordColumnCount = 4;
    private const int KeywordRowCount = 5;
    private const int RequiredKeywordCount = KeywordColumnCount * KeywordRowCount;

    public RenderedWordSearchArtifact RenderTopic(string topic, TextRegionSettings style)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            throw Invalid("Topic cannot be empty.");
        }

        return RenderCenteredText(WordSearchArtifactKind.Topic, topic.Trim().ToUpperInvariant(), style);
    }

    public RenderedWordSearchArtifact RenderKeywordList(IReadOnlyList<string> keywords, TextRegionSettings style)
    {
        ArgumentNullException.ThrowIfNull(keywords);
        ValidateStyle(style);

        if (keywords.Count != RequiredKeywordCount || keywords.Any(string.IsNullOrWhiteSpace))
        {
            throw Invalid($"Keyword list must contain exactly {RequiredKeywordCount} non-empty values.");
        }

        var normalizedKeywords = keywords
            .Select(keyword => keyword.Trim().ToUpperInvariant())
            .ToArray();

        try
        {
            using var font = SystemDrawingRenderSupport.CreateFont(style.Font);
            using var brush = new SolidBrush(SystemDrawingRenderSupport.ParseColor(style.Font.Color));
            using var format = CreateCenteredFormat();
            using var bitmap = SystemDrawingRenderSupport.CreateBitmap(style.Rectangle.Width, style.Rectangle.Height);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                SystemDrawingRenderSupport.Configure(graphics);
                graphics.Clear(Color.White);

                for (var index = 0; index < normalizedKeywords.Length; index++)
                {
                    var cell = GetKeywordCell(index, bitmap.Width, bitmap.Height);
                    EnsureFits(graphics, normalizedKeywords[index], font, cell.Size, $"Keyword {index + 1}");
                    graphics.DrawString(normalizedKeywords[index], font, brush, cell, format);
                }
            }

            return SystemDrawingRenderSupport.EncodePng(WordSearchArtifactKind.KeywordList, bitmap);
        }
        catch (WordSearchGenerationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or ExternalException)
        {
            throw new WordSearchGenerationException("render_failed", $"Keyword list could not be rendered: {exception.Message}", exception);
        }
    }

    public RenderedWordSearchArtifact RenderPageNumber(int topicIndex, TextRegionSettings style)
    {
        if (topicIndex <= 0)
        {
            throw Invalid("Topic index must be positive.");
        }

        return RenderCenteredText(
            WordSearchArtifactKind.PageNumber,
            topicIndex.ToString(CultureInfo.InvariantCulture),
            style);
    }

    internal static RectangleF GetKeywordCell(int index, int width, int height)
    {
        if (index is < 0 or >= RequiredKeywordCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        var cellWidth = width / (float)KeywordColumnCount;
        var cellHeight = height / (float)KeywordRowCount;
        var column = index / KeywordRowCount;
        var row = index % KeywordRowCount;
        return new RectangleF(column * cellWidth, row * cellHeight, cellWidth, cellHeight);
    }

    private static RenderedWordSearchArtifact RenderCenteredText(
        WordSearchArtifactKind kind,
        string text,
        TextRegionSettings style)
    {
        ValidateStyle(style);

        try
        {
            using var font = SystemDrawingRenderSupport.CreateFont(style.Font);
            using var brush = new SolidBrush(SystemDrawingRenderSupport.ParseColor(style.Font.Color));
            using var format = CreateCenteredFormat();
            using var bitmap = SystemDrawingRenderSupport.CreateBitmap(style.Rectangle.Width, style.Rectangle.Height);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                SystemDrawingRenderSupport.Configure(graphics);
                graphics.Clear(Color.White);
                EnsureFits(graphics, text, font, new SizeF(bitmap.Width, bitmap.Height), kind.ToString());
                graphics.DrawString(text, font, brush, new RectangleF(0, 0, bitmap.Width, bitmap.Height), format);
            }

            return SystemDrawingRenderSupport.EncodePng(kind, bitmap);
        }
        catch (WordSearchGenerationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or ExternalException)
        {
            throw new WordSearchGenerationException("render_failed", $"Text artifact could not be rendered: {exception.Message}", exception);
        }
    }

    private static void EnsureFits(Graphics graphics, string text, Font font, SizeF available, string field)
    {
        var measured = graphics.MeasureString(text, font);
        if (measured.Width > available.Width || measured.Height > available.Height)
        {
            throw new WordSearchGenerationException(
                "text_overflow",
                $"{field} text does not fit its configured rectangle using font '{font.Name}' at {font.Size}pt.");
        }
    }

    private static void ValidateStyle(TextRegionSettings style)
    {
        ArgumentNullException.ThrowIfNull(style);
        if (style.Rectangle.Width <= 0 || style.Rectangle.Height <= 0 ||
            style.Font.Size <= 0 || string.IsNullOrWhiteSpace(style.Font.Name))
        {
            throw Invalid("Text rectangle and font must have positive dimensions and a font name.");
        }
    }

    private static StringFormat CreateCenteredFormat() => new()
    {
        Alignment = StringAlignment.Center,
        LineAlignment = StringAlignment.Center,
        FormatFlags = StringFormatFlags.NoWrap
    };

    private static WordSearchGenerationException Invalid(string message) => new("render_input_invalid", message);
}
