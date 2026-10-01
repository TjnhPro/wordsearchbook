using System.Drawing;
using System.Drawing.Imaging;
using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Core.WordSearch.Domain;
using WordSearchBook.Infrastructure.WordSearch.Processing;

namespace WordSearchBook.Infrastructure.Tests.WordSearch;

public sealed class SystemDrawingBookQrPageRendererTests
{
    [Fact]
    public async Task RendersEncodedBookUrlIntoConfiguredSquareWithoutChangingPageSize()
    {
        var root = Path.Combine(Path.GetTempPath(), $"word-search-qr-render-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var templatePath = Path.Combine(root, "page_qr.png");
            var outputPath = Path.Combine(root, "cache", "page-qr.png");
            SaveTemplate(templatePath, Color.LightBlue);

            var result = await new SystemDrawingBookQrPageRenderer().RenderAsync(
                templatePath,
                outputPath,
                "Book Name #1",
                new QrPageSettings(300, 400, 600, "Example.COM"));

            Assert.Equal("https://wordsearch.example.com/Book%20Name%20%231", result.Url);
            Assert.Equal((2588, 3375), (result.Width, result.Height));
            using var output = new Bitmap(outputPath);
            Assert.Equal(ImageFormat.Png.Guid, output.RawFormat.Guid);
            Assert.Equal((2588, 3375), (output.Width, output.Height));
            Assert.InRange(output.HorizontalResolution, 299.5f, 300.5f);
            Assert.Equal(Color.LightBlue.ToArgb(), output.GetPixel(20, 20).ToArgb());
            Assert.True(ContainsDarkPixel(output, new Rectangle(300, 400, 600, 600)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsSquareThatCannotFitOnePixelPerQrModule()
    {
        var root = Path.Combine(Path.GetTempPath(), $"word-search-qr-render-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var templatePath = Path.Combine(root, "page_qr.png");
            SaveTemplate(templatePath, Color.White);

            var exception = await Assert.ThrowsAsync<WordSearchGenerationException>(() =>
                new SystemDrawingBookQrPageRenderer().RenderAsync(
                    templatePath,
                    Path.Combine(root, "page-qr.png"),
                    "sample-book",
                    new QrPageSettings(0, 0, 10, "example.com")));

            Assert.Equal("qr_size_too_small", exception.Code);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static bool ContainsDarkPixel(Bitmap image, Rectangle rectangle)
    {
        for (var y = rectangle.Top; y < rectangle.Bottom; y += 2)
        {
            for (var x = rectangle.Left; x < rectangle.Right; x += 2)
            {
                var pixel = image.GetPixel(x, y);
                if (pixel.R < 40 && pixel.G < 40 && pixel.B < 40) return true;
            }
        }

        return false;
    }

    private static void SaveTemplate(string path, Color color)
    {
        using var image = new Bitmap(2588, 3375, PixelFormat.Format24bppRgb);
        image.SetResolution(300, 300);
        using var graphics = Graphics.FromImage(image);
        graphics.Clear(color);
        image.Save(path, ImageFormat.Png);
    }
}
