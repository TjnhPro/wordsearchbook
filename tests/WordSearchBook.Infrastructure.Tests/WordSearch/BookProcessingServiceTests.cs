using Microsoft.Extensions.DependencyInjection;
using PdfSharp.Pdf.IO;
using System.Drawing;
using System.Drawing.Imaging;
using WordSearchBook.Core.Application.Workspace;
using WordSearchBook.Core.WordSearch.Application;
using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Core.WordSearch.Domain;
using WordSearchBook.Core.WordSearch.Settings;
using WordSearchBook.Core.WordSearch.Validation;
using WordSearchBook.Infrastructure.DependencyInjection;
using WordSearchBook.Infrastructure.WordSearch.Processing;

namespace WordSearchBook.Infrastructure.Tests.WordSearch;

public sealed class BookProcessingServiceTests
{
    [Fact]
    public void NaturalPageOrderingPlacesNumericTwoBeforeTen()
    {
        var ordered = new[] { "page-10.png", "Page-1.png", "page-2.png" }
            .OrderBy(value => value, BookProcessingService.NaturalFileNameComparer.Instance)
            .ToArray();

        Assert.Equal(["Page-1.png", "page-2.png", "page-10.png"], ordered);
    }

    [Fact]
    public async Task PublishesPuzzlePdfAndQuality85AnswerJpegsAtPrintDimensions()
    {
        var root = CopyFixtureToTemporaryRoot();
        try
        {
            SaveImage(Path.Combine(root, "brands", "demo", "page_layout.png"), Color.White);
            SaveFrontOverlay(Path.Combine(root, "brands", "demo", "front_layout.png"));
            var front = Path.Combine(root, "brands", "demo", "front");
            var back = Path.Combine(root, "brands", "demo", "back");
            Directory.CreateDirectory(front);
            Directory.CreateDirectory(back);
            SaveImage(Path.Combine(front, "page-10.png"), Color.LightBlue);
            SaveImage(Path.Combine(front, "page-2.png"), Color.LightGreen);
            SaveImage(Path.Combine(back, "closing.jpg"), Color.LightGray, ImageFormat.Jpeg);

            using var services = BuildServices();
            Assert.True((await services.GetRequiredService<IBookDataValidationService>()
                .ValidateAsync(root, "sample-book")).IsSuccess);
            Assert.True((await services.GetRequiredService<IBrandValidationService>()
                .ValidateAsync(root, "demo")).IsSuccess);

            var result = await services.GetRequiredService<IBookProcessingService>().ProcessAsync(
                new BookProcessingRequest(root, "sample-book", "demo"));

            Assert.True(File.Exists(result.PdfPath));
            Assert.Equal(1, result.PuzzlePageCount);
            Assert.Equal(2, result.FrontPageCount);
            Assert.Equal(1, result.BackPageCount);
            Assert.Equal(4, result.PdfPageCount);
            var answer = Assert.Single(result.Answers);
            Assert.Equal(85, answer.Quality);
            Assert.Equal("answer/001.jpg", answer.RelativePath);
            var answerPath = Path.Combine(root, "input", "sample-book", "output", "answer", "001.jpg");
            using (var image = new Bitmap(answerPath))
            {
                Assert.Equal(ImageFormat.Jpeg.Guid, image.RawFormat.Guid);
                Assert.Equal((2588, 3375), (image.Width, image.Height));
                Assert.InRange(image.HorizontalResolution, 299.5f, 300.5f);
                Assert.NotEqual(Color.Lime.ToArgb(), image.GetPixel(210, 410).ToArgb());
            }

            var cachedPagePath = Path.Combine(root, "input", "sample-book", ".workspace", "cache", "topics", "001", "page.png");
            using (var cachedPage = new Bitmap(cachedPagePath))
            {
                Assert.Equal(Color.Lime.ToArgb(), cachedPage.GetPixel(210, 410).ToArgb());
            }

            using (var pdf = PdfReader.Open(result.PdfPath, PdfDocumentOpenMode.Import))
            {
                Assert.Equal(4, pdf.PageCount);
                Assert.All(pdf.Pages.Cast<PdfSharp.Pdf.PdfPage>(), page =>
                {
                    Assert.InRange(page.Width.Point, 621.11, 621.13);
                    Assert.InRange(page.Height.Point, 809.99, 810.01);
                });
            }

            Assert.True(File.Exists(Path.Combine(root, "input", "sample-book", ".workspace", "output.manifest.json")));
            var dataState = await services.GetRequiredService<IBookDataValidationService>()
                .CheckStateAsync(root, "sample-book");
            var brandState = await services.GetRequiredService<IBrandValidationService>()
                .CheckStateAsync(root, "demo");
            var outputState = await services.GetRequiredService<IBookOutputSnapshotService>().ReadAsync(
                root,
                "sample-book",
                "demo",
                dataState,
                new Dictionary<string, BrandValidationState>(StringComparer.OrdinalIgnoreCase) { ["demo"] = brandState });
            Assert.Equal(BookOutputStatus.Ready, outputState.Status);
            Assert.Equal((1, 2, 1, 4, 1), (
                outputState.PuzzlePageCount,
                outputState.FrontPageCount,
                outputState.BackPageCount,
                outputState.PdfPageCount,
                outputState.AnswerCount));

            await File.AppendAllTextAsync(answerPath, "changed");
            outputState = await services.GetRequiredService<IBookOutputSnapshotService>().ReadAsync(
                root,
                "sample-book",
                "demo",
                dataState,
                new Dictionary<string, BrandValidationState>(StringComparer.OrdinalIgnoreCase) { ["demo"] = brandState });
            Assert.Equal(BookOutputStatus.Stale, outputState.Status);
            Assert.Equal("output_answer_changed", outputState.ReasonCode);
            await File.WriteAllTextAsync(Path.Combine(result.AnswerDirectory, "999.jpg"), "stale");

            await services.GetRequiredService<IBookProcessingService>().ProcessAsync(
                new BookProcessingRequest(root, "sample-book", "demo"));

            Assert.False(File.Exists(Path.Combine(result.AnswerDirectory, "999.jpg")));
            var publishedPdf = await File.ReadAllBytesAsync(result.PdfPath);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                services.GetRequiredService<IBookProcessingService>().ProcessAsync(
                    new BookProcessingRequest(root, "sample-book", "demo"),
                    cancellationToken: cancellation.Token));
            Assert.Equal(publishedPdf, await File.ReadAllBytesAsync(result.PdfPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ContentHashGateDetectsCsvTamperingEvenWhenMetadataIsRestored()
    {
        var root = CopyFixtureToTemporaryRoot();
        try
        {
            using var services = BuildServices();
            var validationService = services.GetRequiredService<IBookDataValidationService>();
            Assert.True((await validationService.ValidateAsync(root, "sample-book")).IsSuccess);
            var dataPath = Path.Combine(root, "input", "sample-book", "data.csv");
            var before = new FileInfo(dataPath);
            var originalTime = before.LastWriteTimeUtc;
            var content = await File.ReadAllTextAsync(dataPath);
            Assert.Contains("Amazing Animals", content, StringComparison.Ordinal);
            await File.WriteAllTextAsync(dataPath, content.Replace("Amazing Animals", "AMAZING ANIMALS", StringComparison.Ordinal));
            File.SetLastWriteTimeUtc(dataPath, originalTime);
            Assert.Equal(before.Length, new FileInfo(dataPath).Length);
            Assert.Equal(BookDataValidationStatus.Validated, (await validationService.CheckStateAsync(root, "sample-book")).Status);

            var exception = await Assert.ThrowsAsync<WordSearchGenerationException>(() =>
                services.GetRequiredService<IBookProcessingService>().ProcessAsync(
                    new BookProcessingRequest(root, "sample-book", "demo")));

            Assert.Equal("book_data_not_validated", exception.Code);
            Assert.False(Directory.Exists(Path.Combine(root, "input", "sample-book", "output")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessesMultipleTopicsIntoOrderedCacheAnswersAndPdf()
    {
        var root = CopyFixtureToTemporaryRoot();
        try
        {
            var dataPath = Path.Combine(root, "input", "sample-book", "data.csv");
            var lines = await File.ReadAllLinesAsync(dataPath);
            var secondTopicRows = lines.Skip(1)
                .Select(line => line
                    .Replace("PUZ-001", "PUZ-002", StringComparison.Ordinal)
                    .Replace(",Amazing Animals,", ",Ocean Life,", StringComparison.Ordinal))
                .ToArray();
            await File.WriteAllLinesAsync(dataPath, [lines[0], .. lines.Skip(1), .. secondTopicRows]);
            SaveImage(Path.Combine(root, "brands", "demo", "page_layout.png"), Color.White);

            using var services = BuildServices();
            Assert.True((await services.GetRequiredService<IBookDataValidationService>()
                .ValidateAsync(root, "sample-book")).IsSuccess);
            Assert.True((await services.GetRequiredService<IBrandValidationService>()
                .ValidateAsync(root, "demo")).IsSuccess);

            var result = await services.GetRequiredService<IBookProcessingService>().ProcessAsync(
                new BookProcessingRequest(root, "sample-book", "demo"));

            Assert.Equal(2, result.PuzzlePageCount);
            Assert.Equal([1, 2], result.Answers.Select(answer => answer.TopicIndex));
            Assert.Equal(["answer/001.jpg", "answer/002.jpg"], result.Answers.Select(answer => answer.RelativePath));
            using (var pdf = PdfReader.Open(result.PdfPath, PdfDocumentOpenMode.Import))
            {
                Assert.Equal(2, pdf.PageCount);
            }

            var cache = Path.Combine(root, "input", "sample-book", ".workspace", "cache");
            Assert.True(File.Exists(Path.Combine(cache, "manifest.json")));
            Assert.True(File.Exists(Path.Combine(cache, "topics", "001", "page.png")));
            Assert.True(File.Exists(Path.Combine(cache, "topics", "002", "page.png")));
            Assert.Equal(["topics"], Directory.EnumerateDirectories(cache).Select(Path.GetFileName));
            Assert.False(Directory.Exists(Path.Combine(root, "input", "sample-book", ".workspace", "pdf-work")));
            Assert.Empty(Directory.EnumerateFiles(Path.Combine(root, "input", "sample-book", "output"), "*.pending", SearchOption.AllDirectories));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AppendsOptionalQrPageAndRemovesStaleQrCacheWhenTemplateIsRemoved()
    {
        var root = CopyFixtureToTemporaryRoot();
        try
        {
            using var services = BuildServices();
            var settingsReader = services.GetRequiredService<IWordSearchSettingsReader>();
            var settingsWriter = services.GetRequiredService<IWordSearchSettingsWriter>();
            var current = await settingsReader.ReadAsync(root, "demo");
            var qrTemplatePath = Path.Combine(root, "brands", "demo", "page_qr.png");
            SaveImage(qrTemplatePath, Color.LightPink);
            await settingsWriter.SaveBrandAsync(
                root,
                "demo",
                current.Brand with { QrPage = new QrPageSettings(300, 400, 600, "Example.COM") });
            Assert.True((await services.GetRequiredService<IBookDataValidationService>()
                .ValidateAsync(root, "sample-book")).IsSuccess);
            Assert.True((await services.GetRequiredService<IBrandValidationService>()
                .ValidateAsync(root, "demo")).IsSuccess);

            var processingService = services.GetRequiredService<IBookProcessingService>();
            var result = await processingService.ProcessAsync(new BookProcessingRequest(root, "sample-book", "demo"));

            Assert.Equal(1, result.QrPageCount);
            Assert.Equal(2, result.PdfPageCount);
            var qrCachePath = Path.Combine(root, "input", "sample-book", ".workspace", "cache", "page-qr.png");
            Assert.True(File.Exists(qrCachePath));
            using (var qrPage = new Bitmap(qrCachePath))
            {
                Assert.Equal(Color.LightPink.ToArgb(), qrPage.GetPixel(20, 20).ToArgb());
            }

            var manifestPath = Path.Combine(root, "input", "sample-book", ".workspace", "output.manifest.json");
            using (var manifest = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath)))
            {
                Assert.Equal(2, manifest.RootElement.GetProperty("schemaVersion").GetInt32());
                Assert.Equal(1, manifest.RootElement.GetProperty("qrPageCount").GetInt32());
            }

            File.Delete(qrTemplatePath);
            current = await settingsReader.ReadAsync(root, "demo");
            await settingsWriter.SaveBrandAsync(root, "demo", current.Brand with { QrPage = null });
            Assert.True((await services.GetRequiredService<IBrandValidationService>()
                .ValidateAsync(root, "demo")).IsSuccess);

            result = await processingService.ProcessAsync(new BookProcessingRequest(root, "sample-book", "demo"));

            Assert.Equal(0, result.QrPageCount);
            Assert.Equal(1, result.PdfPageCount);
            Assert.False(File.Exists(qrCachePath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddWordSearchBookInfrastructure();
        return services.BuildServiceProvider();
    }

    private static void SaveImage(string path, Color color, ImageFormat? format = null)
    {
        using var image = new Bitmap(2588, 3375, PixelFormat.Format24bppRgb);
        image.SetResolution(300, 300);
        using var graphics = Graphics.FromImage(image);
        graphics.Clear(color);
        image.Save(path, format ?? ImageFormat.Png);
    }

    private static void SaveTransparentFrontLayout(string path)
    {
        using var image = new Bitmap(2588, 3375, PixelFormat.Format32bppArgb);
        image.SetResolution(300, 300);
        using (var graphics = Graphics.FromImage(image))
        {
            graphics.Clear(Color.Transparent);
        }

        image.Save(path, ImageFormat.Png);
    }

    private static void SaveFrontOverlay(string path)
    {
        using var image = new Bitmap(2588, 3375, PixelFormat.Format32bppArgb);
        image.SetResolution(300, 300);
        using (var graphics = Graphics.FromImage(image))
        {
            graphics.Clear(Color.Transparent);
            using var brush = new SolidBrush(Color.Lime);
            graphics.FillRectangle(brush, 205, 405, 20, 20);
        }

        image.Save(path, ImageFormat.Png);
    }

    private static string CopyFixtureToTemporaryRoot()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "TestData", "SingleTopicBook");
        var destination = Path.Combine(Path.GetTempPath(), $"word-search-processing-{Guid.NewGuid():N}");
        Directory.CreateDirectory(destination);
        foreach (var sourceFile in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(source, sourceFile);
            var segments = relativePath.Split(Path.DirectorySeparatorChar);
            if (segments.Contains(".workspace", StringComparer.OrdinalIgnoreCase) ||
                segments.Contains("output", StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var destinationFile = Path.Combine(destination, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            File.Copy(sourceFile, destinationFile);
        }

        var brandDirectory = Path.Combine(destination, "brands", "demo");
        SaveImage(Path.Combine(brandDirectory, "page_layout.png"), Color.White);
        SaveTransparentFrontLayout(Path.Combine(brandDirectory, "front_layout.png"));

        return destination;
    }
}
