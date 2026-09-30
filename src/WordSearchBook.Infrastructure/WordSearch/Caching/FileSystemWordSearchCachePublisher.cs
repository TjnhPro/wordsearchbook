using System.Text.Json;
using WordSearchBook.Core.WordSearch.Caching;
using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Core.WordSearch.Domain;

namespace WordSearchBook.Infrastructure.WordSearch.Caching;

public sealed class FileSystemWordSearchCachePublisher : IWordSearchCachePublisher
{
    private const int ManifestSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<WordSearchGenerationResult> PublishAsync(
        WordSearchGenerationRequest request,
        WordSearchSettingsBundle settings,
        IReadOnlyList<WordSearchTopicArtifactSet> topics,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(topics);

        var cacheRoot = Path.Combine(
            request.RootPath,
            "input",
            request.BookId,
            ".workspace",
            "cache");
        var finalDirectory = Path.Combine(cacheRoot, request.BrandId);
        var stagingDirectory = Path.Combine(cacheRoot, $".{request.BrandId}.staging-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(stagingDirectory);
            var generatedTopics = new List<GeneratedWordSearchTopic>(topics.Count);
            var manifestTopics = new List<ManifestTopic>(topics.Count);

            foreach (var topicSet in topics)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var artifactByKind = ValidateArtifacts(topicSet);
                var relativeTopicDirectory = Path.Combine("topics", topicSet.Topic.Index.ToString("000"));
                var topicDirectory = Path.Combine(stagingDirectory, relativeTopicDirectory);
                Directory.CreateDirectory(topicDirectory);

                var publishedArtifacts = new List<WordSearchArtifact>(artifactByKind.Count);
                var manifestArtifactPaths = new Dictionary<string, string>(StringComparer.Ordinal);

                foreach (var kind in Enum.GetValues<WordSearchArtifactKind>())
                {
                    var rendered = artifactByKind[kind];
                    var fileName = FileName(kind);
                    var relativePath = Path.Combine(relativeTopicDirectory, fileName).Replace('\\', '/');
                    await File.WriteAllBytesAsync(
                        Path.Combine(topicDirectory, fileName),
                        rendered.Content,
                        cancellationToken);
                    publishedArtifacts.Add(new WordSearchArtifact(kind, relativePath, rendered.Width, rendered.Height));
                    manifestArtifactPaths.Add(ManifestName(kind), relativePath);
                }

                generatedTopics.Add(new GeneratedWordSearchTopic(
                    topicSet.Topic.Index,
                    topicSet.Topic.Name,
                    publishedArtifacts,
                    topicSet.Placements));
                manifestTopics.Add(new ManifestTopic(
                    topicSet.Topic.Index,
                    topicSet.Topic.Name,
                    topicSet.Topic.Entries.Select(entry => new ManifestEntry(
                        entry.SourceRow,
                        entry.Keyword,
                        entry.WordSearchKey)).ToArray(),
                    manifestArtifactPaths,
                    topicSet.Placements.Select(placement => new ManifestPlacement(
                        placement.WordSearchKey,
                        placement.Cells.Select(cell => new ManifestCell(cell.X, cell.Y)).ToArray())).ToArray()));
            }

            var manifest = new ManifestDocument(
                ManifestSchemaVersion,
                request.BookId,
                request.BrandId,
                settings.Global.Board,
                settings.Global.Page,
                manifestTopics);
            var stagingManifestPath = Path.Combine(stagingDirectory, "manifest.json");
            await using (var manifestStream = File.Create(stagingManifestPath))
            {
                await JsonSerializer.SerializeAsync(manifestStream, manifest, JsonOptions, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            ReplaceDirectory(stagingDirectory, finalDirectory);
            return new WordSearchGenerationResult(
                request.BookId,
                request.BrandId,
                Path.Combine(finalDirectory, "manifest.json"),
                generatedTopics);
        }
        catch (WordSearchGenerationException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new WordSearchGenerationException("cache_publish_failed", $"Word search cache could not be published: {exception.Message}", exception);
        }
        finally
        {
            TryDeleteDirectory(stagingDirectory);
        }
    }

    private static Dictionary<WordSearchArtifactKind, RenderedWordSearchArtifact> ValidateArtifacts(
        WordSearchTopicArtifactSet topicSet)
    {
        Dictionary<WordSearchArtifactKind, RenderedWordSearchArtifact> artifacts;
        try
        {
            artifacts = topicSet.Artifacts.ToDictionary(artifact => artifact.Kind);
        }
        catch (ArgumentException exception)
        {
            throw new WordSearchGenerationException(
                "artifact_set_invalid",
                $"Topic {topicSet.Topic.Index} contains duplicate artifact kinds.",
                exception);
        }

        var missing = Enum.GetValues<WordSearchArtifactKind>()
            .Where(kind => !artifacts.ContainsKey(kind))
            .ToArray();
        if (missing.Length > 0)
        {
            throw new WordSearchGenerationException(
                "artifact_set_invalid",
                $"Topic {topicSet.Topic.Index} is missing artifact(s): {string.Join(", ", missing)}.");
        }

        return artifacts;
    }

    private static void ReplaceDirectory(string stagingDirectory, string finalDirectory)
    {
        if (!Directory.Exists(finalDirectory))
        {
            Directory.Move(stagingDirectory, finalDirectory);
            return;
        }

        var backupDirectory = $"{finalDirectory}.backup-{Guid.NewGuid():N}";
        Directory.Move(finalDirectory, backupDirectory);
        try
        {
            Directory.Move(stagingDirectory, finalDirectory);
        }
        catch
        {
            Directory.Move(backupDirectory, finalDirectory);
            throw;
        }

        TryDeleteDirectory(backupDirectory);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Stale staging/backup data can be cleaned by a later maintenance pass.
        }
    }

    private static string FileName(WordSearchArtifactKind kind) => kind switch
    {
        WordSearchArtifactKind.BoardGame => "board-game.png",
        WordSearchArtifactKind.BoardGameAnswer => "board-game-answer.png",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    private static string ManifestName(WordSearchArtifactKind kind) => kind switch
    {
        WordSearchArtifactKind.BoardGame => "boardGame",
        WordSearchArtifactKind.BoardGameAnswer => "boardGameAnswer",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    private sealed record ManifestDocument(
        int SchemaVersion,
        string BookId,
        string BrandId,
        BoardSize Board,
        PageSize Page,
        IReadOnlyList<ManifestTopic> Topics);

    private sealed record ManifestTopic(
        int Index,
        string Name,
        IReadOnlyList<ManifestEntry> Entries,
        IReadOnlyDictionary<string, string> Artifacts,
        IReadOnlyList<ManifestPlacement> Placements);

    private sealed record ManifestEntry(int SourceRow, string Keyword, string WordSearchKey);

    private sealed record ManifestPlacement(string WordSearchKey, IReadOnlyList<ManifestCell> Cells);

    private sealed record ManifestCell(int X, int Y);
}
