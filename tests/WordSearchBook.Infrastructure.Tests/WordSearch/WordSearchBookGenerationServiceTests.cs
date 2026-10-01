using Microsoft.Extensions.DependencyInjection;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using WordSearchBook.Core.WordSearch.Application;
using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Core.WordSearch.Validation;
using WordSearchBook.Infrastructure.DependencyInjection;

namespace WordSearchBook.Infrastructure.Tests.WordSearch;

public sealed class WordSearchBookGenerationServiceTests
{
    [Fact]
    public async Task GeneratesBoardAndPageArtifactsAndManifestThenAtomicallyReplacesCache()
    {
        var root = CopyFixtureToTemporaryRoot();
        try
        {
            using var services = BuildServices();
            await CertifyBrandAsync(services, root);
            var service = services.GetRequiredService<IWordSearchBookGenerationService>();
            var request = new WordSearchGenerationRequest(root, "sample-book", "demo");

            var result = await service.GenerateAsync(request);

            var topic = Assert.Single(result.Topics);
            Assert.Equal(4, topic.Artifacts.Count);
            Assert.Equal(
                [
                    WordSearchArtifactKind.BoardGame,
                    WordSearchArtifactKind.BoardGameAnswer,
                    WordSearchArtifactKind.Page,
                    WordSearchArtifactKind.PageAnswer
                ],
                topic.Artifacts.Select(artifact => artifact.Kind));
            Assert.Equal(20, topic.Placements.Count);
            Assert.True(File.Exists(result.ManifestPath));
            var cacheDirectory = Path.GetDirectoryName(result.ManifestPath)!;
            Assert.All(topic.Artifacts, artifact =>
                Assert.True(File.Exists(Path.Combine(cacheDirectory, artifact.RelativePath))));

            using (var manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(result.ManifestPath)))
            {
                Assert.Equal(1, manifest.RootElement.GetProperty("schemaVersion").GetInt32());
                Assert.Equal("sample-book", manifest.RootElement.GetProperty("bookId").GetString());
                Assert.Equal("AMAZING ANIMALS", manifest.RootElement.GetProperty("topics")[0].GetProperty("name").GetString());
                Assert.Equal("RED PANDA", manifest.RootElement.GetProperty("topics")[0].GetProperty("entries")[0].GetProperty("keyword").GetString());
                Assert.Equal("REDPANDA", manifest.RootElement.GetProperty("topics")[0].GetProperty("entries")[0].GetProperty("wordSearchKey").GetString());
                Assert.Equal(20, manifest.RootElement.GetProperty("topics")[0].GetProperty("entries").GetArrayLength());
                Assert.Equal(20, manifest.RootElement.GetProperty("topics")[0].GetProperty("placements").GetArrayLength());
                Assert.Contains(
                    manifest.RootElement.GetProperty("topics")[0].GetProperty("placements").EnumerateArray(),
                    placement => placement.GetProperty("wordSearchKey").GetString() == "REDPANDA");
                Assert.Equal(4, manifest.RootElement.GetProperty("topics")[0].GetProperty("artifacts").EnumerateObject().Count());
                Assert.Equal("topics/001/page.png", manifest.RootElement.GetProperty("topics")[0].GetProperty("artifacts").GetProperty("page").GetString());
                Assert.Equal("topics/001/page-answer.png", manifest.RootElement.GetProperty("topics")[0].GetProperty("artifacts").GetProperty("pageAnswer").GetString());
            }

            var sentinel = Path.Combine(cacheDirectory, "old-cache.txt");
            await File.WriteAllTextAsync(sentinel, "old");
            var secondResult = await service.GenerateAsync(request);

            Assert.False(File.Exists(sentinel));
            Assert.True(File.Exists(secondResult.ManifestPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FailedRerunKeepsLastSuccessfulCache()
    {
        var root = CopyFixtureToTemporaryRoot();
        try
        {
            using var services = BuildServices();
            await CertifyBrandAsync(services, root);
            var service = services.GetRequiredService<IWordSearchBookGenerationService>();
            var request = new WordSearchGenerationRequest(root, "sample-book", "demo");
            var successful = await service.GenerateAsync(request);
            var manifestBefore = await File.ReadAllBytesAsync(successful.ManifestPath);
            var boardPath = Path.Combine(
                Path.GetDirectoryName(successful.ManifestPath)!,
                successful.Topics[0].Artifacts.Single(artifact => artifact.Kind == WordSearchArtifactKind.BoardGame).RelativePath);
            var boardBefore = await File.ReadAllBytesAsync(boardPath);

            await File.WriteAllLinesAsync(
                Path.Combine(root, "input", "sample-book", "data.csv"),
                ["Topic,Keyword,Word Search Key", "Broken,Only One,ONLYONE"]);

            var exception = await Assert.ThrowsAsync<WordSearchGenerationException>(() => service.GenerateAsync(request));

            Assert.Equal("topic_word_count_invalid", exception.Code);
            Assert.Equal(manifestBefore, await File.ReadAllBytesAsync(successful.ManifestPath));
            Assert.Equal(boardBefore, await File.ReadAllBytesAsync(boardPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task GeneratesIndependentArtifactsForMultipleTopics()
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

            using var services = BuildServices();
            await CertifyBrandAsync(services, root);
            var result = await services.GetRequiredService<IWordSearchBookGenerationService>()
                .GenerateAsync(new WordSearchGenerationRequest(root, "sample-book", "demo"));

            Assert.Equal(2, result.Topics.Count);
            Assert.Equal([1, 2], result.Topics.Select(topic => topic.Index));
            Assert.All(result.Topics, topic => Assert.Equal(4, topic.Artifacts.Count));
            Assert.Contains(result.Topics[1].Artifacts, artifact => artifact.RelativePath.StartsWith("topics/002/", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task HonorsPreCanceledRequestWithoutCreatingCache()
    {
        var root = CopyFixtureToTemporaryRoot();
        try
        {
            using var services = BuildServices();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                services.GetRequiredService<IWordSearchBookGenerationService>().GenerateAsync(
                    new WordSearchGenerationRequest(root, "sample-book", "demo"),
                    cancellation.Token));

            Assert.False(Directory.Exists(Path.Combine(root, "input", "sample-book", ".workspace")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsGenerationBeforeReadingInputWhenLayoutIsNotCertified()
    {
        var root = CopyFixtureToTemporaryRoot();
        try
        {
            File.Delete(Path.Combine(root, "input", "sample-book", "data.csv"));
            using var services = BuildServices();

            var exception = await Assert.ThrowsAsync<WordSearchGenerationException>(() =>
                services.GetRequiredService<IWordSearchBookGenerationService>().GenerateAsync(
                    new WordSearchGenerationRequest(root, "sample-book", "demo")));

            Assert.Equal("brand_not_validated", exception.Code);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsGenerationWhenCertifiedLayoutMetadataChanges()
    {
        var root = CopyFixtureToTemporaryRoot();
        try
        {
            using var services = BuildServices();
            await CertifyBrandAsync(services, root);
            await using (var stream = new FileStream(
                Path.Combine(root, "brands", "demo", "page_layout.png"),
                FileMode.Append,
                FileAccess.Write,
                FileShare.None))
            {
                await stream.WriteAsync(new byte[] { 0 });
            }

            var exception = await Assert.ThrowsAsync<WordSearchGenerationException>(() =>
                services.GetRequiredService<IWordSearchBookGenerationService>().GenerateAsync(
                    new WordSearchGenerationRequest(root, "sample-book", "demo")));

            Assert.Equal("brand_not_validated", exception.Code);
            Assert.Contains("brand_fingerprint_changed", exception.Message, StringComparison.Ordinal);
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

    [Fact]
    public async Task RejectsGenerationWhenOptionalAssetIsAddedAfterCertification()
    {
        var root = CopyFixtureToTemporaryRoot();
        try
        {
            using var services = BuildServices();
            await CertifyBrandAsync(services, root);
            var front = Path.Combine(root, "brands", "demo", "front");
            Directory.CreateDirectory(front);
            using (var image = new Bitmap(2588, 3375))
            {
                image.Save(Path.Combine(front, "opening.jpg"), ImageFormat.Jpeg);
            }

            var exception = await Assert.ThrowsAsync<WordSearchGenerationException>(() =>
                services.GetRequiredService<IWordSearchBookGenerationService>().GenerateAsync(
                    new WordSearchGenerationRequest(root, "sample-book", "demo")));

            Assert.Equal("brand_not_validated", exception.Code);
            Assert.Contains("brand_fingerprint_changed", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task CertifyBrandAsync(IServiceProvider services, string root)
    {
        var result = await services.GetRequiredService<IBrandValidationService>().ValidateAsync(root, "demo");
        Assert.True(result.IsSuccess);
    }

    private static string CopyFixtureToTemporaryRoot()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "TestData", "SingleTopicBook");
        var destination = Path.Combine(Path.GetTempPath(), $"word-search-book-{Guid.NewGuid():N}");
        Directory.CreateDirectory(destination);

        foreach (var sourceFile in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(source, sourceFile);
            if (relativePath.Split(Path.DirectorySeparatorChar).Contains(".workspace", StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var destinationFile = Path.Combine(destination, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            File.Copy(sourceFile, destinationFile);
        }

        var layoutPath = Path.Combine(destination, "brands", "demo", "page_layout.png");
        using (var layout = new Bitmap(2588, 3375, PixelFormat.Format32bppArgb))
        {
            using var graphics = Graphics.FromImage(layout);
            graphics.Clear(Color.White);
            layout.Save(layoutPath, ImageFormat.Png);
        }

        return destination;
    }
}
