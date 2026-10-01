using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using QRCoder;
using WordSearchBook.Core.WordSearch.Application;
using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Core.WordSearch.Domain;

namespace WordSearchBook.Infrastructure.WordSearch.Processing;

public sealed class SystemDrawingBookQrPageRenderer : IBookQrPageRenderer
{
    private const int PageWidth = WordSearchSettingsDefaults.PageWidth;
    private const int PageHeight = WordSearchSettingsDefaults.PageHeight;

    public Task<RenderedBookQrPage> RenderAsync(
        string templatePath,
        string outputPath,
        string bookFolderName,
        QrPageSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templatePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(bookFolderName);
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();
        var url = QrPageUrlBuilder.Build(settings.DomainName, bookFolderName);
        var temporaryPath = $"{outputPath}.{Guid.NewGuid():N}.tmp";

        try
        {
            using var template = LoadTemplate(templatePath);
            using var qrData = QRCodeGenerator.GenerateQrCode(url, QRCodeGenerator.ECCLevel.Q);
            var moduleCount = qrData.ModuleMatrix.Count;
            var pixelsPerModule = settings.Size / moduleCount;
            if (pixelsPerModule < 1)
            {
                throw new WordSearchGenerationException(
                    "qr_size_too_small",
                    $"QR Size {settings.Size} is too small for URL '{url}'. Increase Size in Brand Settings.");
            }

            using var qrCode = new PngByteQRCode(qrData);
            var qrBytes = qrCode.GetGraphic(pixelsPerModule, drawQuietZones: true);
            using var qrStream = new MemoryStream(qrBytes, writable: false);
            using var qrImage = Image.FromStream(qrStream, useEmbeddedColorManagement: false, validateImageData: true);
            var qrX = settings.X + ((settings.Size - qrImage.Width) / 2);
            var qrY = settings.Y + ((settings.Size - qrImage.Height) / 2);

            using var page = new Bitmap(PageWidth, PageHeight, PixelFormat.Format32bppArgb);
            page.SetResolution(300, 300);
            using (var graphics = Graphics.FromImage(page))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.DrawImageUnscaled(template, 0, 0);
                graphics.CompositingMode = CompositingMode.SourceOver;
                graphics.FillRectangle(Brushes.White, settings.X, settings.Y, settings.Size, settings.Size);
                graphics.DrawImage(
                    qrImage,
                    new Rectangle(qrX, qrY, qrImage.Width, qrImage.Height),
                    0,
                    0,
                    qrImage.Width,
                    qrImage.Height,
                    GraphicsUnit.Pixel);
            }

            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            page.Save(temporaryPath, ImageFormat.Png);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, outputPath, overwrite: true);
            return Task.FromResult(new RenderedBookQrPage(outputPath, url, PageWidth, PageHeight));
        }
        catch (WordSearchGenerationException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or ExternalException or OutOfMemoryException)
        {
            throw new WordSearchGenerationException(
                "qr_page_render_failed",
                $"QR page could not be rendered: {exception.Message}",
                exception);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static Bitmap LoadTemplate(string path)
    {
        if (!File.Exists(path))
        {
            throw new WordSearchGenerationException("qr_page_not_found", $"QR page template was not found: {path}");
        }

        using var source = Image.FromFile(path, useEmbeddedColorManagement: false);
        if (source.RawFormat.Guid != ImageFormat.Png.Guid || source.Width != PageWidth || source.Height != PageHeight)
        {
            throw new WordSearchGenerationException(
                "qr_page_invalid",
                $"QR page template must be a {PageWidth}x{PageHeight} PNG image.");
        }

        var copy = new Bitmap(PageWidth, PageHeight, PixelFormat.Format32bppArgb);
        copy.SetResolution(300, 300);
        using var graphics = Graphics.FromImage(copy);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.DrawImageUnscaled(source, 0, 0);
        return copy;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A later render can clean up an abandoned temporary file.
        }
    }
}

internal static class QrPageUrlBuilder
{
    internal static string Build(string domainName, string bookFolderName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domainName);
        ArgumentException.ThrowIfNullOrWhiteSpace(bookFolderName);
        return $"https://wordsearch.{domainName.Trim().ToLowerInvariant()}/{Uri.EscapeDataString(bookFolderName)}";
    }
}
