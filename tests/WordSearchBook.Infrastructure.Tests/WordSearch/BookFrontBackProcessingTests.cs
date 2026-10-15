using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using WordSearchBook.Core.Application.Workspace;
using WordSearchBook.Core.WordSearch.Application;
using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Core.WordSearch.Domain;
using WordSearchBook.Core.WordSearch.Settings;
using WordSearchBook.Core.WordSearch.Validation;
using WordSearchBook.Infrastructure.WordSearch.Processing;

namespace WordSearchBook.Infrastructure.Tests.WordSearch;

public sealed partial class BookProcessingServiceTests
{
    [Fact]
    public async Task AssemblesAllSixGroupsAndTracksBookAssetChanges()
    {
        var root = CopyFixtureToTemporaryRoot();
        try
        {
            var book = Path.Combine(root, "input", "sample-book");
            var brand = Path.Combine(root, "brands", "demo");
            foreach (var directory in new[] { book, brand })
            {
                Directory.CreateDirectory(Path.Combine(directory, "front"));
                Directory.CreateDirectory(Path.Combine(directory, "back"));
                SaveImage(Path.Combine(directory, "front", "10.png"), Color.Red);
                SaveImage(Path.Combine(directory, "front", "2.JPG"), Color.Green, ImageFormat.Jpeg);
                SaveImage(Path.Combine(directory, "front", "1.png"), Color.Blue);
                SaveImage(Path.Combine(directory, "back", "10.png"), Color.Yellow);
                SaveImage(Path.Combine(directory, "back", "2.jpeg"), Color.Pink, ImageFormat.Jpeg);
            }

            await File.WriteAllTextAsync(Path.Combine(book, "front", "readme.txt"), "ignored");
            var subfolder = Directory.CreateDirectory(Path.Combine(book, "front", "subfolder")).FullName;
            await File.WriteAllTextAsync(Path.Combine(subfolder, "corrupt.png"), "ignored");
            var exporter = new ObservingPdfExporter();
            using var services = BuildServices(exporter);
            var settings = await services.GetRequiredService<IWordSearchSettingsReader>().ReadAsync(root, "demo");
            SaveImage(Path.Combine(brand, "page_qr.png"), Color.White);
            await services.GetRequiredService<IWordSearchSettingsWriter>().SaveBrandAsync(
                root, "demo", settings.Brand with { QrPage = new QrPageSettings(300, 400, 600, "example.com") });
            await ValidateProcessingInputsAsync(services, root);
            var processor = services.GetRequiredService<IBookProcessingService>();
            var result = await processor.ProcessAsync(new(root, "sample-book", "demo"));

            var expected = new[]
            {
                "input/sample-book/front/1.png", "input/sample-book/front/2.JPG", "input/sample-book/front/10.png",
                "brands/demo/front/1.png", "brands/demo/front/2.JPG", "brands/demo/front/10.png",
                "input/sample-book/.workspace/cache/topics/001/page.png",
                "brands/demo/back/2.jpeg", "brands/demo/back/10.png",
                "input/sample-book/back/2.jpeg", "input/sample-book/back/10.png",
                "input/sample-book/.workspace/cache/page-qr.png"
            };
            Assert.Equal(expected, exporter.Pages.Select(path => Path.GetRelativePath(root, path).Replace('\\', '/')));
            Assert.Equal((3, 2, 3, 2, 1, 12), (result.BookFrontPageCount, result.BookBackPageCount,
                result.FrontPageCount, result.BackPageCount, result.QrPageCount, result.PdfPageCount));
            using (var pdf = PdfSharp.Pdf.IO.PdfReader.Open(result.PdfPath, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import))
            {
                Assert.Equal(12, pdf.PageCount);
            }
            Assert.Single(result.Answers);
            var ready = await ReadOutputAsync(services, root);
            Assert.Equal(BookOutputStatus.Ready, ready.Status);
            Assert.Equal((3, 2), (ready.BookFrontPageCount, ready.BookBackPageCount));

            // Files that do not participate in the PDF do not invalidate output.
            await File.WriteAllTextAsync(Path.Combine(book, "front", "notes.txt"), "ignored");
            var nested = Directory.CreateDirectory(Path.Combine(book, "back", "nested")).FullName;
            await File.WriteAllTextAsync(Path.Combine(nested, "invalid.png"), "ignored");
            Assert.Equal(BookOutputStatus.Ready, (await ReadOutputAsync(services, root)).Status);

            var added = Path.Combine(book, "front", "extra.png");
            SaveImage(added, Color.Orange);
            await AssertBookAssetsStaleAsync();
            File.Delete(added);
            Assert.Equal(BookOutputStatus.Ready, (await ReadOutputAsync(services, root)).Status);

            var original = Path.Combine(book, "front", "1.png");
            var renamed = Path.Combine(book, "front", "renamed.png");
            File.Move(original, renamed);
            await AssertBookAssetsStaleAsync();
            File.Move(renamed, original);
            var originalTime = File.GetLastWriteTimeUtc(original);
            File.SetLastWriteTimeUtc(original, originalTime.AddSeconds(10));
            await AssertBookAssetsStaleAsync();
            File.SetLastWriteTimeUtc(original, originalTime);
            File.Delete(original);
            await AssertBookAssetsStaleAsync();

            var manifestPath = Path.Combine(book, ".workspace", "output.manifest.json");
            var manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath))!;
            Assert.Equal(3, manifest["schemaVersion"]!.GetValue<int>());
            Assert.Equal(3, manifest["bookFrontPageCount"]!.GetValue<int>());
            Assert.False(string.IsNullOrWhiteSpace(manifest["bookAssetsFingerprint"]!.GetValue<string>()));
            manifest["schemaVersion"] = 2;
            manifest.AsObject().Remove("bookFrontPageCount");
            manifest.AsObject().Remove("bookBackPageCount");
            manifest.AsObject().Remove("bookAssetsFingerprint");
            await File.WriteAllTextAsync(manifestPath, manifest.ToJsonString());
            var oldOutput = await ReadOutputAsync(services, root);
            Assert.Equal(BookOutputStatus.Stale, oldOutput.Status);
            Assert.Equal("output_manifest_outdated", oldOutput.ReasonCode);

            async Task AssertBookAssetsStaleAsync()
            {
                var output = await ReadOutputAsync(services, root);
                Assert.Equal(BookOutputStatus.Stale, output.Status);
                Assert.Equal("output_book_assets_changed", output.ReasonCode);
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task BookFoldersAreOptionalIndependently(bool hasFront, bool hasBack)
    {
        var root = CopyFixtureToTemporaryRoot();
        try
        {
            var book = Path.Combine(root, "input", "sample-book");
            // Front exists but may be empty; back may be entirely absent.
            Directory.CreateDirectory(Path.Combine(book, "front"));
            if (hasFront) SaveImage(Path.Combine(book, "front", "1.png"), Color.Red);
            if (hasBack)
            {
                Directory.CreateDirectory(Path.Combine(book, "back"));
                SaveImage(Path.Combine(book, "back", "1.png"), Color.Blue);
            }
            using var services = BuildServices();
            await ValidateProcessingInputsAsync(services, root);
            var result = await services.GetRequiredService<IBookProcessingService>().ProcessAsync(new(root, "sample-book", "demo"));
            Assert.Equal(hasFront ? 1 : 0, result.BookFrontPageCount);
            Assert.Equal(hasBack ? 1 : 0, result.BookBackPageCount);
            Assert.Equal(1 + result.BookFrontPageCount + result.BookBackPageCount, result.PdfPageCount);
            Assert.Equal(BookOutputStatus.Ready, (await ReadOutputAsync(services, root)).Status);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("corrupt")]
    [InlineData("dimensions")]
    [InlineData("format")]
    public async Task InvalidBookImageFailsBeforeGenerationAndPreservesPublishedOutput(string failure)
    {
        var root = CopyFixtureToTemporaryRoot();
        try
        {
            using var services = BuildServices();
            await ValidateProcessingInputsAsync(services, root);
            var processor = services.GetRequiredService<IBookProcessingService>();
            var result = await processor.ProcessAsync(new(root, "sample-book", "demo"));
            var before = CapturePublishedFiles(root);
            var cacheManifest = Path.Combine(root, "input", "sample-book", ".workspace", "cache", "manifest.json");
            var cacheTime = File.GetLastWriteTimeUtc(cacheManifest);
            var front = Directory.CreateDirectory(Path.Combine(root, "input", "sample-book", "front")).FullName;
            var path = Path.Combine(front, "invalid.png");
            if (failure == "corrupt") await File.WriteAllTextAsync(path, "not an image");
            else if (failure == "format") SaveImage(path, Color.Red, ImageFormat.Jpeg);
            else
            {
                using var image = new Bitmap(20, 20);
                image.Save(path, ImageFormat.Png);
            }

            var error = await Assert.ThrowsAsync<WordSearchGenerationException>(() => processor.ProcessAsync(new(root, "sample-book", "demo")));
            Assert.Equal("book_page_asset_invalid", error.Code);
            Assert.Contains(path, error.Message, StringComparison.Ordinal);
            Assert.Equal(cacheTime, File.GetLastWriteTimeUtc(cacheManifest));
            AssertPublishedFilesUnchanged(before);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("add")]
    [InlineData("delete")]
    [InlineData("rename")]
    [InlineData("modify")]
    [InlineData("cancel")]
    public async Task ChangesDuringProcessingPreventPublication(string change)
    {
        var root = CopyFixtureToTemporaryRoot();
        try
        {
            var front = Directory.CreateDirectory(Path.Combine(root, "input", "sample-book", "front")).FullName;
            var path = Path.Combine(front, "1.png");
            SaveImage(path, Color.Red);
            var exporter = new ObservingPdfExporter();
            using var services = BuildServices(exporter);
            await ValidateProcessingInputsAsync(services, root);
            var processor = services.GetRequiredService<IBookProcessingService>();
            await processor.ProcessAsync(new(root, "sample-book", "demo"));
            var before = CapturePublishedFiles(root);
            using var cancellation = new CancellationTokenSource();
            exporter.AfterPrepare = () =>
            {
                switch (change)
                {
                    case "add": SaveImage(Path.Combine(front, "2.png"), Color.Blue); break;
                    case "delete": File.Delete(path); break;
                    case "rename": File.Move(path, Path.Combine(front, "renamed.png")); break;
                    case "modify": File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(10)); break;
                    case "cancel": cancellation.Cancel(); break;
                }
            };
            if (change == "cancel")
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processor.ProcessAsync(
                    new(root, "sample-book", "demo"), cancellationToken: cancellation.Token));
            }
            else
            {
                var error = await Assert.ThrowsAsync<WordSearchGenerationException>(() => processor.ProcessAsync(new(root, "sample-book", "demo")));
                Assert.Equal("book_assets_changed_during_processing", error.Code);
            }
            AssertPublishedFilesUnchanged(before);
            Assert.Empty(Directory.EnumerateFiles(Path.Combine(root, "input", "sample-book", "output"), "*.pending", SearchOption.AllDirectories));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static async Task ValidateProcessingInputsAsync(ServiceProvider services, string root)
    {
        Assert.True((await services.GetRequiredService<IBookDataValidationService>().ValidateAsync(root, "sample-book")).IsSuccess);
        Assert.True((await services.GetRequiredService<IBrandValidationService>().ValidateAsync(root, "demo")).IsSuccess);
    }

    private static async Task<WorkspaceBookOutput> ReadOutputAsync(ServiceProvider services, string root) =>
        await services.GetRequiredService<IBookOutputSnapshotService>().ReadAsync(
            root, "sample-book", "demo",
            await services.GetRequiredService<IBookDataValidationService>().CheckStateAsync(root, "sample-book"),
            new Dictionary<string, BrandValidationState> { ["demo"] = await services.GetRequiredService<IBrandValidationService>().CheckStateAsync(root, "demo") });

    private static Dictionary<string, byte[]> CapturePublishedFiles(string root)
    {
        var book = Path.Combine(root, "input", "sample-book");
        return Directory.EnumerateFiles(Path.Combine(book, "output"), "*", SearchOption.AllDirectories)
            .Append(Path.Combine(book, ".workspace", "output.manifest.json"))
            .ToDictionary(path => path, File.ReadAllBytes);
    }

    private static void AssertPublishedFilesUnchanged(Dictionary<string, byte[]> before)
    {
        foreach (var (path, bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    private sealed class ObservingPdfExporter : IBookInteriorPdfExporter
    {
        public IReadOnlyList<string> Pages { get; private set; } = [];
        public Action? AfterPrepare { get; set; }

        public async Task<PreparedInteriorPdf> PrepareAsync(IReadOnlyList<string> orderedPages,
            string workDirectory, string pendingPath, string finalPath, int maximumConcurrency,
            IProgress<BookProcessingProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            Pages = orderedPages.ToArray();
            var result = await new PdfSharpBookInteriorPdfExporter().PrepareAsync(
                orderedPages, workDirectory, pendingPath, finalPath, maximumConcurrency, progress, cancellationToken);
            AfterPrepare?.Invoke();
            return result;
        }
    }
}
