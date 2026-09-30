using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using WordSearchBook.Core.WordSearch.Application;
using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Infrastructure.DependencyInjection;

namespace WordSearchBook.Infrastructure.Tests.WordSearch;

public sealed class WordSearchBookGenerationServiceTests
{
    [Fact]
    public async Task GeneratesTwoBoardArtifactsAndManifestThenAtomicallyReplacesCache()
    {
        var root = CopyFixtureToTemporaryRoot();
        try
        {
            using var services = BuildServices();
            var service = services.GetRequiredService<IWordSearchBookGenerationService>();
            var request = new WordSearchGenerationRequest(root, "sample-book", "demo");

            var result = await service.GenerateAsync(request);

            var topic = Assert.Single(result.Topics);
            Assert.Equal(2, topic.Artifacts.Count);
            Assert.Equal(
                [WordSearchArtifactKind.BoardGame, WordSearchArtifactKind.BoardGameAnswer],
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
                Assert.Equal(20, manifest.RootElement.GetProperty("topics")[0].GetProperty("entries").GetArrayLength());
                Assert.Equal(20, manifest.RootElement.GetProperty("topics")[0].GetProperty("placements").GetArrayLength());
                Assert.Equal(2, manifest.RootElement.GetProperty("topics")[0].GetProperty("artifacts").EnumerateObject().Count());
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
            var result = await services.GetRequiredService<IWordSearchBookGenerationService>()
                .GenerateAsync(new WordSearchGenerationRequest(root, "sample-book", "demo"));

            Assert.Equal(2, result.Topics.Count);
            Assert.Equal([1, 2], result.Topics.Select(topic => topic.Index));
            Assert.All(result.Topics, topic => Assert.Equal(2, topic.Artifacts.Count));
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

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddWordSearchBookInfrastructure();
        return services.BuildServiceProvider();
    }

    private static string CopyFixtureToTemporaryRoot()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "TestData", "SingleTopicBook");
        var destination = Path.Combine(Path.GetTempPath(), $"word-search-book-{Guid.NewGuid():N}");
        Directory.CreateDirectory(destination);

        foreach (var sourceFile in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(source, sourceFile);
            var destinationFile = Path.Combine(destination, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            File.Copy(sourceFile, destinationFile);
        }

        return destination;
    }
}
