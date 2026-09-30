using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Core.WordSearch.Domain;
using WordSearchBook.Core.WordSearch.Rendering;

namespace WordSearchBook.Infrastructure.WordSearch.Rendering;

public sealed class SystemDrawingWordSearchBoardRenderer : IWordSearchBoardRenderer
{
    private const float OutputDpi = 300f;

    public RenderedWordSearchArtifact RenderData(
        WordSearchPuzzle puzzle,
        BoardSize boardSize,
        TextRegionSettings boardStyle)
    {
        ValidateInputs(puzzle, boardSize, boardStyle);
        return Render(WordSearchArtifactKind.BoardGame, puzzle, boardSize, boardStyle, answerLine: null);
    }

    public RenderedWordSearchArtifact RenderAnswer(
        WordSearchPuzzle puzzle,
        BoardSize boardSize,
        TextRegionSettings boardStyle,
        AnswerLineSettings answerLine)
    {
        ValidateInputs(puzzle, boardSize, boardStyle);
        ArgumentNullException.ThrowIfNull(answerLine);

        if (answerLine.Width <= 0)
        {
            throw Invalid("Answer line width must be positive.");
        }

        return Render(WordSearchArtifactKind.BoardGameAnswer, puzzle, boardSize, boardStyle, answerLine);
    }

    private static RenderedWordSearchArtifact Render(
        WordSearchArtifactKind kind,
        WordSearchPuzzle puzzle,
        BoardSize boardSize,
        TextRegionSettings boardStyle,
        AnswerLineSettings? answerLine)
    {
        try
        {
            var rectangle = boardStyle.Rectangle;
            var cellSize = rectangle.Width / boardSize.Width;
            using var font = CreateFont(boardStyle.Font);
            using var textBrush = new SolidBrush(ParseColor(boardStyle.Font.Color));
            using var bitmap = new Bitmap(rectangle.Width, rectangle.Height, PixelFormat.Format32bppArgb);
            bitmap.SetResolution(OutputDpi, OutputDpi);

            using (var graphics = Graphics.FromImage(bitmap))
            {
                ConfigureGraphics(graphics);
                graphics.Clear(Color.White);
                EnsureGlyphsFit(graphics, font, puzzle.Data, cellSize);
                DrawGrid(graphics, puzzle.Data, font, textBrush, cellSize);

                if (answerLine is not null)
                {
                    using var pen = new Pen(ParseColor(answerLine.Color), answerLine.Width)
                    {
                        EndCap = LineCap.Round,
                        StartCap = LineCap.Round,
                        Alignment = PenAlignment.Center
                    };

                    foreach (var placement in puzzle.Placements)
                    {
                        var first = placement.Cells[0];
                        var last = placement.Cells[^1];
                        graphics.DrawLine(
                            pen,
                            Center(first.X, first.Y, cellSize),
                            Center(last.X, last.Y, cellSize));
                    }

                    DrawAnswerLetters(graphics, puzzle.Answer, font, textBrush, cellSize);
                }
            }

            using var output = new MemoryStream();
            bitmap.Save(output, ImageFormat.Png);
            return new RenderedWordSearchArtifact(kind, output.ToArray(), bitmap.Width, bitmap.Height);
        }
        catch (WordSearchGenerationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or ExternalException)
        {
            throw new WordSearchGenerationException("render_failed", $"Board image could not be rendered: {exception.Message}", exception);
        }
    }

    private static void ValidateInputs(WordSearchPuzzle puzzle, BoardSize boardSize, TextRegionSettings boardStyle)
    {
        ArgumentNullException.ThrowIfNull(puzzle);
        ArgumentNullException.ThrowIfNull(boardSize);
        ArgumentNullException.ThrowIfNull(boardStyle);

        if (boardSize.Width <= 0 || boardSize.Height <= 0 ||
            puzzle.Data.GetLength(0) != boardSize.Width || puzzle.Data.GetLength(1) != boardSize.Height ||
            puzzle.Answer.GetLength(0) != boardSize.Width || puzzle.Answer.GetLength(1) != boardSize.Height)
        {
            throw Invalid("Puzzle dimensions must match the configured board size.");
        }

        var rectangle = boardStyle.Rectangle;
        if (rectangle.Width <= 0 || rectangle.Height <= 0 || rectangle.Width != rectangle.Height ||
            rectangle.Width % boardSize.Width != 0 || rectangle.Height % boardSize.Height != 0)
        {
            throw Invalid("Board rectangle must be square and evenly divisible by the board dimensions.");
        }

        if (boardStyle.Font.Size <= 0 || string.IsNullOrWhiteSpace(boardStyle.Font.Name))
        {
            throw Invalid("Board font name and size are required.");
        }

        foreach (var placement in puzzle.Placements)
        {
            if (placement.Cells.Count == 0 || placement.Cells.Any(cell =>
                    cell.X < 0 || cell.X >= boardSize.Width || cell.Y < 0 || cell.Y >= boardSize.Height))
            {
                throw Invalid($"Placement '{placement.WordSearchKey}' contains an invalid cell.");
            }
        }
    }

    private static Font CreateFont(FontSettings settings)
    {
        var familyName = FontFamily.Families
            .Select(family => family.Name)
            .FirstOrDefault(name => string.Equals(name, settings.Name, StringComparison.OrdinalIgnoreCase));

        if (familyName is null)
        {
            throw new WordSearchGenerationException("font_unavailable", $"Font '{settings.Name}' is not installed.");
        }

        return new Font(familyName, settings.Size, FontStyle.Regular);
    }

    private static void EnsureGlyphsFit(Graphics graphics, Font font, char[,] data, int cellSize)
    {
        foreach (var character in data.Cast<char>().Where(character => character != '\0').Distinct())
        {
            var size = graphics.MeasureString(character.ToString(), font);
            if (size.Width > cellSize || size.Height > cellSize)
            {
                throw new WordSearchGenerationException(
                    "text_overflow",
                    $"Character '{character}' using font '{font.Name}' at {font.Size}pt does not fit a {cellSize}px board cell.");
            }
        }
    }

    private static void DrawGrid(Graphics graphics, char[,] grid, Font font, Brush brush, int cellSize)
    {
        for (var x = 0; x < grid.GetLength(0); x++)
        {
            for (var y = 0; y < grid.GetLength(1); y++)
            {
                DrawCharacter(graphics, grid[x, y], x, y, font, brush, cellSize);
            }
        }
    }

    private static void DrawAnswerLetters(Graphics graphics, char[,] answer, Font font, Brush brush, int cellSize)
    {
        for (var x = 0; x < answer.GetLength(0); x++)
        {
            for (var y = 0; y < answer.GetLength(1); y++)
            {
                if (answer[x, y] != '\0')
                {
                    DrawCharacter(graphics, answer[x, y], x, y, font, brush, cellSize);
                }
            }
        }
    }

    private static void DrawCharacter(Graphics graphics, char character, int x, int y, Font font, Brush brush, int cellSize)
    {
        var value = character.ToString();
        var size = graphics.MeasureString(value, font);
        var centerX = (x * cellSize) + (cellSize / 2f);
        var centerY = (y * cellSize) + (cellSize / 2f);
        graphics.DrawString(value, font, brush, centerX - (size.Width / 2f), centerY - (size.Height / 2f));
    }

    private static PointF Center(int x, int y, int cellSize) =>
        new((x * cellSize) + (cellSize / 2f), (y * cellSize) + (cellSize / 2f));

    private static void ConfigureGraphics(Graphics graphics)
    {
        graphics.SmoothingMode = SmoothingMode.None;
        graphics.PixelOffsetMode = PixelOffsetMode.None;
        graphics.CompositingQuality = CompositingQuality.HighSpeed;
        graphics.TextRenderingHint = TextRenderingHint.AntiAlias;
    }

    private static Color ParseColor(string value)
    {
        if (value.Length == 7)
        {
            return Color.FromArgb(
                255,
                int.Parse(value.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                int.Parse(value.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                int.Parse(value.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }

        if (value.Length == 9)
        {
            return Color.FromArgb(
                int.Parse(value.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                int.Parse(value.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                int.Parse(value.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                int.Parse(value.AsSpan(7, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }

        throw Invalid($"Color '{value}' is invalid.");
    }

    private static WordSearchGenerationException Invalid(string message) => new("render_input_invalid", message);
}
