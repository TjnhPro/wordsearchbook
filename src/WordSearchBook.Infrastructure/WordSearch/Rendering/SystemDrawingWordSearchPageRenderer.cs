using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Core.WordSearch.Domain;
using WordSearchBook.Core.WordSearch.Rendering;

namespace WordSearchBook.Infrastructure.WordSearch.Rendering;

public sealed class SystemDrawingWordSearchPageRenderer : IWordSearchPageRenderer
{
    private const int ExpectedKeywordCount = 20;
    private const int KeywordsPerColumn = 5;

    public RenderedWordSearchArtifact Render(
        string pageLayoutPath,
        WordSearchTopic topic,
        int pageNumber,
        RenderedWordSearchArtifact boardArtifact,
        WordSearchSettingsBundle settings,
        WordSearchArtifactKind outputKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageLayoutPath);
        ArgumentNullException.ThrowIfNull(topic);
        ArgumentNullException.ThrowIfNull(boardArtifact);
        ArgumentNullException.ThrowIfNull(settings);
        ValidateInputs(topic, pageNumber, boardArtifact, settings, outputKind);

        using var layout = LoadLayout(pageLayoutPath, settings.Global.Page);
        try
        {
            using var boardStream = new MemoryStream(boardArtifact.Content, writable: false);
            using var board = Image.FromStream(boardStream, useEmbeddedColorManagement: false, validateImageData: true);
            var boardRectangle = settings.Brand.BoardGame.Rectangle;
            if (board.Width != boardRectangle.Width || board.Height != boardRectangle.Height)
            {
                throw Invalid("Board artifact dimensions must match boardGame.rectangle.");
            }

            using var page = SystemDrawingRenderSupport.CreateBitmap(settings.Global.Page.Width, settings.Global.Page.Height);
            using (var graphics = Graphics.FromImage(page))
            {
                SystemDrawingRenderSupport.Configure(graphics);
                graphics.DrawImageUnscaled(layout, 0, 0);
                graphics.DrawImageUnscaled(board, boardRectangle.X, boardRectangle.Y);
                DrawPageText(graphics, topic, pageNumber, settings);
            }

            return SystemDrawingRenderSupport.EncodePng(outputKind, page);
        }
        catch (WordSearchGenerationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or ExternalException or OutOfMemoryException)
        {
            throw new WordSearchGenerationException(
                "render_failed",
                $"Page image could not be rendered: {exception.Message}",
                exception);
        }
    }

    private static Bitmap LoadLayout(string path, PageSize pageSize)
    {
        if (!File.Exists(path))
        {
            throw new WordSearchGenerationException("page_layout_not_found", $"Page layout was not found: {path}");
        }

        try
        {
            using var source = Image.FromFile(path, useEmbeddedColorManagement: false);
            if (source.RawFormat.Guid != ImageFormat.Png.Guid ||
                source.Width != pageSize.Width || source.Height != pageSize.Height)
            {
                throw new WordSearchGenerationException(
                    "page_layout_invalid",
                    $"Page layout must be a {pageSize.Width}x{pageSize.Height} PNG image.");
            }

            var copy = SystemDrawingRenderSupport.CreateBitmap(source.Width, source.Height);
            using var graphics = Graphics.FromImage(copy);
            graphics.DrawImageUnscaled(source, 0, 0);
            return copy;
        }
        catch (WordSearchGenerationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or OutOfMemoryException or ExternalException)
        {
            throw new WordSearchGenerationException("page_layout_invalid", $"Page layout is not a readable PNG image: {path}", exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new WordSearchGenerationException("page_layout_read_failed", $"Page layout could not be read: {path}", exception);
        }
    }

    private static void DrawPageText(
        Graphics graphics,
        WordSearchTopic topic,
        int pageNumber,
        WordSearchSettingsBundle settings)
    {
        using var topicFont = SystemDrawingRenderSupport.CreateFont(settings.Brand.Topic.Font);
        using var topicBrush = new SolidBrush(SystemDrawingRenderSupport.ParseColor(settings.Brand.Topic.Font.Color));
        DrawAnchoredText(
            graphics,
            topic.Name,
            settings.Brand.Topic.X,
            settings.Brand.Topic.Y,
            settings.Brand.Topic.Alignment,
            topicFont,
            topicBrush,
            settings.Global.Page);

        DrawKeywords(graphics, topic, settings);

        using var pageFont = SystemDrawingRenderSupport.CreateFont(settings.Brand.PageNumber.Font);
        using var pageBrush = new SolidBrush(SystemDrawingRenderSupport.ParseColor(settings.Brand.PageNumber.Font.Color));
        DrawAnchoredText(
            graphics,
            pageNumber.ToString(CultureInfo.InvariantCulture),
            settings.Brand.PageNumber.X,
            settings.Brand.PageNumber.Y,
            settings.Brand.PageNumber.Alignment,
            pageFont,
            pageBrush,
            settings.Global.Page);
    }

    private static void DrawKeywords(
        Graphics graphics,
        WordSearchTopic topic,
        WordSearchSettingsBundle settings)
    {
        var keywordSettings = settings.Brand.KeywordList;
        using var font = SystemDrawingRenderSupport.CreateFont(keywordSettings.Font);
        using var brush = new SolidBrush(SystemDrawingRenderSupport.ParseColor(keywordSettings.Font.Color));
        var measured = new List<(string Value, RectangleF Bounds)>(ExpectedKeywordCount);

        for (var index = 0; index < topic.Entries.Count; index++)
        {
            var columnIndex = index / KeywordsPerColumn;
            var rowIndex = index % KeywordsPerColumn;
            var anchor = keywordSettings.Columns[columnIndex];
            var bounds = MeasureAnchoredText(
                graphics,
                topic.Entries[index].Keyword,
                anchor.X,
                anchor.Y + (rowIndex * keywordSettings.StepY),
                keywordSettings.Alignment,
                font,
                settings.Global.Page);
            EnsureWithinKeywordSlot(
                topic.Entries[index].Keyword,
                bounds,
                columnIndex,
                keywordSettings,
                settings.Global.Page);
            if (measured.Any(existing => existing.Bounds.IntersectsWith(bounds)))
            {
                throw new WordSearchGenerationException(
                    "page_text_overflow",
                    $"Keyword '{topic.Entries[index].Keyword}' crosses a neighboring keyword slot.");
            }

            measured.Add((topic.Entries[index].Keyword, bounds));
        }

        foreach (var item in measured)
        {
            graphics.DrawString(item.Value, font, brush, item.Bounds.X, item.Bounds.Y);
        }
    }

    private static void EnsureWithinKeywordSlot(
        string value,
        RectangleF bounds,
        int columnIndex,
        KeywordListSettings settings,
        PageSize pageSize)
    {
        var ordered = settings.Columns
            .Select((column, index) => (column.X, Index: index))
            .OrderBy(column => column.X)
            .ToArray();
        var orderedIndex = Array.FindIndex(ordered, column => column.Index == columnIndex);
        var left = orderedIndex == 0
            ? 0f
            : (ordered[orderedIndex - 1].X + ordered[orderedIndex].X) / 2f;
        var right = orderedIndex == ordered.Length - 1
            ? pageSize.Width
            : (ordered[orderedIndex].X + ordered[orderedIndex + 1].X) / 2f;

        if (bounds.Left < left || bounds.Right > right || bounds.Height > settings.StepY)
        {
            throw new WordSearchGenerationException(
                "page_text_overflow",
                $"Keyword '{value}' crosses a neighboring keyword slot.");
        }
    }

    private static void DrawAnchoredText(
        Graphics graphics,
        string value,
        int x,
        int y,
        TextAlignment alignment,
        Font font,
        Brush brush,
        PageSize pageSize)
    {
        var bounds = MeasureAnchoredText(graphics, value, x, y, alignment, font, pageSize);
        graphics.DrawString(value, font, brush, bounds.X, bounds.Y);
    }

    private static RectangleF MeasureAnchoredText(
        Graphics graphics,
        string value,
        int x,
        int y,
        TextAlignment alignment,
        Font font,
        PageSize pageSize)
    {
        var size = graphics.MeasureString(value, font);
        var left = alignment switch
        {
            TextAlignment.Left => x,
            TextAlignment.Center => x - (size.Width / 2f),
            TextAlignment.Right => x - size.Width,
            _ => throw Invalid("Text alignment is invalid.")
        };
        var bounds = new RectangleF(left, y, size.Width, size.Height);
        if (bounds.Left < 0 || bounds.Top < 0 || bounds.Right > pageSize.Width || bounds.Bottom > pageSize.Height)
        {
            throw new WordSearchGenerationException(
                "page_text_overflow",
                $"Text '{value}' crosses the page boundary.");
        }

        return bounds;
    }

    private static void ValidateInputs(
        WordSearchTopic topic,
        int pageNumber,
        RenderedWordSearchArtifact boardArtifact,
        WordSearchSettingsBundle settings,
        WordSearchArtifactKind outputKind)
    {
        if (topic.Entries.Count != ExpectedKeywordCount)
        {
            throw Invalid($"A composed page requires exactly {ExpectedKeywordCount} keywords.");
        }

        if (pageNumber is < 1 or > 999)
        {
            throw Invalid("Page number must be between 1 and 999.");
        }

        var validPair = (outputKind, boardArtifact.Kind) switch
        {
            (WordSearchArtifactKind.Page, WordSearchArtifactKind.BoardGame) => true,
            (WordSearchArtifactKind.PageAnswer, WordSearchArtifactKind.BoardGameAnswer) => true,
            _ => false
        };
        if (!validPair)
        {
            throw Invalid("Page artifact kind does not match its board artifact kind.");
        }

        if (settings.Global.Page.Width != WordSearchSettingsDefaults.PageWidth ||
            settings.Global.Page.Height != WordSearchSettingsDefaults.PageHeight)
        {
            throw Invalid($"Page size must be {WordSearchSettingsDefaults.PageWidth}x{WordSearchSettingsDefaults.PageHeight}.");
        }
    }

    private static WordSearchGenerationException Invalid(string message) => new("render_input_invalid", message);
}
