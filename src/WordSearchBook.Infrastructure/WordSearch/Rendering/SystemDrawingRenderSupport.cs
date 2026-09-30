using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Core.WordSearch.Domain;

namespace WordSearchBook.Infrastructure.WordSearch.Rendering;

internal static class SystemDrawingRenderSupport
{
    private const float OutputDpi = 300f;

    internal static Bitmap CreateBitmap(int width, int height)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        bitmap.SetResolution(OutputDpi, OutputDpi);
        return bitmap;
    }

    internal static Font CreateFont(FontSettings settings)
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

    internal static void Configure(Graphics graphics)
    {
        graphics.SmoothingMode = SmoothingMode.None;
        graphics.PixelOffsetMode = PixelOffsetMode.None;
        graphics.CompositingQuality = CompositingQuality.HighSpeed;
        graphics.TextRenderingHint = TextRenderingHint.AntiAlias;
    }

    internal static Color ParseColor(string value)
    {
        if (value.Length == 7)
        {
            return Color.FromArgb(
                255,
                ParseByte(value, 1),
                ParseByte(value, 3),
                ParseByte(value, 5));
        }

        if (value.Length == 9)
        {
            return Color.FromArgb(
                ParseByte(value, 1),
                ParseByte(value, 3),
                ParseByte(value, 5),
                ParseByte(value, 7));
        }

        throw new WordSearchGenerationException("render_input_invalid", $"Color '{value}' is invalid.");
    }

    internal static RenderedWordSearchArtifact EncodePng(WordSearchArtifactKind kind, Bitmap bitmap)
    {
        using var output = new MemoryStream();
        bitmap.Save(output, ImageFormat.Png);
        return new RenderedWordSearchArtifact(kind, output.ToArray(), bitmap.Width, bitmap.Height);
    }

    private static int ParseByte(string value, int start) =>
        int.Parse(value.AsSpan(start, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
}
