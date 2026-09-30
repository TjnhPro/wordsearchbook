using Microsoft.Extensions.DependencyInjection;
using PdfSharp.Pdf.IO;
using System.Drawing;
using System.Drawing.Imaging;
using WordSearchBook.Core.Application.Workspace;
using WordSearchBook.Core.WordSearch.Application;
using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Core.WordSearch.Validation;
using WordSearchBook.Infrastructure.DependencyInjection;

namespace WordSearchBook.Infrastructure.Tests.WordSearch;

public sealed class BookProcessingServiceTests
{
    [Fact]
    public async Task PublishesPuzzlePdfAndQuality85AnswerJpegsAtPrintDimensions()
    {
        var root = CopyFixtureToTemporaryRoot();
        try
        {
            SaveImage(Path.Combine(root, "brands", "demo", "page_layout.png"), Color.White);
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
            using (var image = Image.FromFile(answerPath))
            {
                Assert.Equal(ImageFormat.Jpeg.Guid, image.RawFormat.Guid);
                Assert.Equal((2588, 3375), (image.Width, image.Height));
                Assert.InRange(image.HorizontalResolution, 299.5f, 300.5f);
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

        return destination;
    }
}
