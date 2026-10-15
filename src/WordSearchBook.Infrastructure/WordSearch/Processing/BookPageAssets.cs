using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Core.WordSearch.Validation;

namespace WordSearchBook.Infrastructure.WordSearch.Processing;

internal sealed record BookPageAssets(
    IReadOnlyList<string> Front,
    IReadOnlyList<string> Back,
    string Fingerprint)
{
    public static BookPageAssets Capture(string bookDirectory)
    {
        var front = Discover(Path.Combine(bookDirectory, "front"));
        var back = Discover(Path.Combine(bookDirectory, "back"));
        var metadata = front.Concat(back).Select(path =>
        {
            var file = new FileInfo(path);
            return new BrandValidationFileMetadata(
                Path.GetRelativePath(bookDirectory, path),
                file.Length,
                new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero));
        });
        return new BookPageAssets(front, back, BrandAssetFingerprintCalculator.Calculate(metadata));
    }

    public void Validate(CancellationToken cancellationToken)
    {
        foreach (var path in Front.Concat(Back))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var image = Image.FromFile(path);
                var expectedFormat = Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase)
                    ? ImageFormat.Png : ImageFormat.Jpeg;
                if (image.RawFormat.Guid != expectedFormat.Guid)
                {
                    throw new InvalidDataException("Image format must match its PNG or JPEG extension.");
                }

                if (image.Width != BrandValidationDefinition.PageWidth || image.Height != BrandValidationDefinition.PageHeight)
                {
                    throw new InvalidDataException("Image must be 2588 x 3375 pixels.");
                }
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or
                UnauthorizedAccessException or ArgumentException or OutOfMemoryException or ExternalException)
            {
                throw new WordSearchGenerationException(
                    "book_page_asset_invalid",
                    $"Book page image is invalid: {path}. {exception.Message}",
                    exception);
            }
        }
    }

    private static IReadOnlyList<string> Discover(string directory) => !Directory.Exists(directory)
        ? []
        : Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .Where(path => BrandValidationDefinition.SupportedImageExtensions.Contains(
                Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .OrderBy(Path.GetFileName, BookProcessingService.NaturalFileNameComparer.Instance)
            .ToArray();
}
