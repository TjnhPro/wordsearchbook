using System.Drawing;
using System.Drawing.Drawing2D;
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
        string frontLayoutPath,
        WordSearchTopic topic,
        int pageNumber,
        RenderedWordSearchArtifact boardArtifact,
        WordSearchSettingsBundle settings,
        WordSearchArtifactKind outputKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageLayoutPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(frontLayoutPath);
        ArgumentNullException.ThrowIfNull(topic);
        ArgumentNullException.ThrowIfNull(boardArtifact);
        ArgumentNullException.ThrowIfNull(settings);
        ValidateInputs(topic, pageNumber, boardArtifact, settings, outputKind);

        using var layout = LoadLayout(pageLayoutPath, settings.Global.Page);
        using var frontLayout = outputKind == WordSearchArtifactKind.Page
            ? LoadLayout(frontLayoutPath, settings.Global.Page, isFrontLayout: true)
            : null;
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
                graphics.CompositingMode = CompositingMode.SourceOver;
                graphics.DrawImageUnscaled(layout, 0, 0);
                graphics.DrawImageUnscaled(board, boardRectangle.X, boardRectangle.Y);
                DrawPageText(graphics, topic, pageNumber, settings);
                if (frontLayout is not null)
                {
                    graphics.DrawImageUnscaled(frontLayout, 0, 0);
                }
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

    private static Bitmap LoadLayout(string path, PageSize pageSize, bool isFrontLayout = false)
    {
        var codePrefix = isFrontLayout ? "front_layout" : "page_layout";
        var displayName = isFrontLayout ? "Front layout" : "Page layout";
        if (!File.Exists(path))
        {
            throw new WordSearchGenerationException($"{codePrefix}_not_found", $"{displayName} was not found: {path}");
        }

        try
        {
            using var source = Image.FromFile(path, useEmbeddedColorManagement: false);
            if (source.RawFormat.Guid != ImageFormat.Png.Guid ||
                source.Width != pageSize.Width || source.Height != pageSize.Height)
            {
                throw new WordSearchGenerationException(
                    $"{codePrefix}_invalid",
                    $"{displayName} must be a {pageSize.Width}x{pageSize.Height} PNG image.");
            }

            var copy = SystemDrawingRenderSupport.CreateBitmap(source.Width, source.Height);
            using var graphics = Graphics.FromImage(copy);
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.DrawImage(
                source,
                new Rectangle(0, 0, source.Width, source.Height),
                0,
                0,
                source.Width,
                source.Height,
                GraphicsUnit.Pixel);
            return copy;
        }
        catch (WordSearchGenerationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or OutOfMemoryException or ExternalException)
        {
            throw new WordSearchGenerationException($"{codePrefix}_invalid", $"{displayName} is not a readable PNG image: {path}", exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new WordSearchGenerationException($"{codePrefix}_read_failed", $"{displayName} could not be read: {path}", exception);
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
        DrawTopic(
            graphics,
            topic.Name,
            settings.Brand.Topic,
            topicFont,
            topicBrush,
            settings.Global.Page);

        DrawKeywords(graphics, topic, settings);

        DrawQuote(graphics, topic, settings.Brand.Quote);

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
            settings.Global.Page,
            $"Page number '{pageNumber}' is outside the printable page. Adjust the Page number position in Brand Settings.");
    }

    private static void DrawQuote(
        Graphics graphics,
        WordSearchTopic topic,
        TextRegionSettings settings)
    {
        using var font = SystemDrawingRenderSupport.CreateFont(settings.Font);
        using var brush = new SolidBrush(SystemDrawingRenderSupport.ParseColor(settings.Font.Color));
        var rectangle = settings.Rectangle;
        var lineHeight = font.GetHeight(graphics);
        var sourceRow = topic.Entries.Count > 0 ? topic.Entries[0].SourceRow : 0;
        var overflowMessage =
            $"CSV row {sourceRow}, topic '{topic.Name}': Quote '{topic.Quote}' does not fit inside the Quote area in at most two lines. Increase Quote Width or Height, reduce the Quote font size, or shorten the Quote.";
        var lines = QuoteLineLayout.Split(
            topic.Quote,
            rectangle.Width,
            rectangle.Height,
            lineHeight,
            value => graphics.MeasureString(value, font).Width,
            overflowMessage);
        var top = rectangle.Y + ((rectangle.Height - (lineHeight * lines.Count)) / 2f);

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var lineWidth = graphics.MeasureString(line, font).Width;
            var left = rectangle.X + ((rectangle.Width - lineWidth) / 2f);
            graphics.DrawString(line, font, brush, left, top + (index * lineHeight));
        }
    }

    private static void DrawTopic(
        Graphics graphics,
        string topicName,
        AnchoredTextSettings settings,
        Font font,
        Brush brush,
        PageSize pageSize)
    {
        var lines = TopicLineLayout.Split(topicName);
        var lineHeight = font.GetHeight(graphics);

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            DrawAnchoredText(
                graphics,
                line,
                settings.X,
                settings.Y + (index * lineHeight),
                settings.Alignment,
                font,
                brush,
                pageSize,
                $"Topic '{topicName}' line {index + 1} '{line}' is outside the printable page. Adjust the Topic position in Brand Settings.");
        }
    }

    private static void DrawKeywords(
        Graphics graphics,
        WordSearchTopic topic,
        WordSearchSettingsBundle settings)
    {
        var keywordSettings = settings.Brand.KeywordList;
        using var font = SystemDrawingRenderSupport.CreateFont(keywordSettings.Font);
        using var brush = new SolidBrush(SystemDrawingRenderSupport.ParseColor(keywordSettings.Font.Color));
        var measured = new List<MeasuredKeyword>(ExpectedKeywordCount);

        for (var index = 0; index < topic.Entries.Count; index++)
        {
            var columnIndex = index / KeywordsPerColumn;
            var rowIndex = index % KeywordsPerColumn;
            var entry = topic.Entries[index];
            var anchor = keywordSettings.Columns[columnIndex];
            var bounds = MeasureAnchoredText(
                graphics,
                entry.Keyword,
                anchor.X,
                anchor.Y + (rowIndex * keywordSettings.StepY),
                keywordSettings.Alignment,
                font,
                settings.Global.Page,
                $"{EntryContext(topic, entry)} is outside the printable page. Adjust the keyword columns or font in Brand Settings.");
            measured.Add(new MeasuredKeyword(entry, columnIndex, rowIndex, bounds));
        }

        EnsureKeywordColumnsDoNotTouch(measured, topic, keywordSettings);
        EnsureKeywordRowsDoNotTouch(measured, topic);

        foreach (var item in measured)
        {
            graphics.DrawString(item.Entry.Keyword, font, brush, item.Bounds.X, item.Bounds.Y);
        }
    }

    private static void EnsureKeywordColumnsDoNotTouch(
        IReadOnlyCollection<MeasuredKeyword> measured,
        WordSearchTopic topic,
        KeywordListSettings settings)
    {
        var orderedColumns = settings.Columns
            .Select((column, index) => (column.X, Index: index))
            .OrderBy(column => column.X)
            .ToArray();

        for (var index = 0; index < orderedColumns.Length - 1; index++)
        {
            var leftColumn = orderedColumns[index];
            var rightColumn = orderedColumns[index + 1];
            var rightMostLeftKeyword = measured
                .Where(keyword => keyword.ColumnIndex == leftColumn.Index)
                .MaxBy(keyword => keyword.Bounds.Right)!;
            var leftMostRightKeyword = measured
                .Where(keyword => keyword.ColumnIndex == rightColumn.Index)
                .MinBy(keyword => keyword.Bounds.Left)!;

            if (rightMostLeftKeyword.Bounds.Right >= leftMostRightKeyword.Bounds.Left)
            {
                throw new WordSearchGenerationException(
                    "keyword_slot_width_overflow",
                    $"{EntryContext(topic, rightMostLeftKeyword.Entry)} touches or overlaps Keyword '{leftMostRightKeyword.Entry.Keyword}' from CSV row {leftMostRightKeyword.Entry.SourceRow} between keyword columns {leftColumn.Index + 1} and {rightColumn.Index + 1}. Shorten either Keyword, reduce the keyword font size, or increase the space between columns in Brand Settings.");
            }
        }
    }

    private static void EnsureKeywordRowsDoNotTouch(
        IReadOnlyCollection<MeasuredKeyword> measured,
        WordSearchTopic topic)
    {
        foreach (var column in measured.GroupBy(keyword => keyword.ColumnIndex))
        {
            var orderedRows = column.OrderBy(keyword => keyword.RowIndex).ToArray();
            for (var index = 0; index < orderedRows.Length - 1; index++)
            {
                var upperKeyword = orderedRows[index];
                var lowerKeyword = orderedRows[index + 1];
                if (upperKeyword.Bounds.Bottom >= lowerKeyword.Bounds.Top)
                {
                    throw new WordSearchGenerationException(
                        "keyword_slot_height_overflow",
                        $"{EntryContext(topic, upperKeyword.Entry)} touches or overlaps Keyword '{lowerKeyword.Entry.Keyword}' from CSV row {lowerKeyword.Entry.SourceRow} in keyword column {upperKeyword.ColumnIndex + 1}. Increase the keyword row spacing or reduce the keyword font size in Brand Settings.");
                }
            }
        }
    }

    private static void DrawAnchoredText(
        Graphics graphics,
        string value,
        float x,
        float y,
        TextAlignment alignment,
        Font font,
        Brush brush,
        PageSize pageSize,
        string boundaryMessage)
    {
        var bounds = MeasureAnchoredText(graphics, value, x, y, alignment, font, pageSize, boundaryMessage);
        graphics.DrawString(value, font, brush, bounds.X, bounds.Y);
    }

    private static RectangleF MeasureAnchoredText(
        Graphics graphics,
        string value,
        float x,
        float y,
        TextAlignment alignment,
        Font font,
        PageSize pageSize,
        string boundaryMessage)
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
                "page_text_boundary_overflow",
                boundaryMessage);
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

    private static string EntryContext(WordSearchTopic topic, WordSearchEntry entry) =>
        $"CSV row {entry.SourceRow}, topic '{topic.Name}': Keyword '{entry.Keyword}'";

    private sealed record MeasuredKeyword(
        WordSearchEntry Entry,
        int ColumnIndex,
        int RowIndex,
        RectangleF Bounds);
}
