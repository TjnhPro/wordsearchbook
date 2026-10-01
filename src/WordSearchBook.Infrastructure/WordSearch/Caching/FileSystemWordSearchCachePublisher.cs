using System.Collections.Concurrent;
using System.Text.Json;
using WordSearchBook.Core.WordSearch.Caching;
using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Core.WordSearch.Domain;

namespace WordSearchBook.Infrastructure.WordSearch.Caching;

public sealed class FileSystemWordSearchCachePublisher : IWordSearchCachePublisher
{
    private const int ManifestSchemaVersion = 2;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public Task<IWordSearchCacheSession> OpenAsync(
        WordSearchGenerationRequest request,
        WordSearchSettingsBundle settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        var cacheRoot = Path.Combine(request.RootPath, "input", request.BookId, ".workspace", "cache");
        try
        {
            Directory.CreateDirectory(Path.Combine(cacheRoot, "topics"));
            DeleteFile(Path.Combine(cacheRoot, "manifest.json"));
            DeleteFile(Path.Combine(cacheRoot, "manifest.pending.json"));
            return Task.FromResult<IWordSearchCacheSession>(new Session(request, settings, cacheRoot));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new WordSearchGenerationException(
                "cache_open_failed",
                $"Word search cache could not be prepared: {exception.Message}",
                exception);
        }
    }

    private sealed class Session(
        WordSearchGenerationRequest request,
        WordSearchSettingsBundle settings,
        string cacheRoot) : IWordSearchCacheSession
    {
        private readonly ConcurrentDictionary<int, ManifestTopic> manifestTopics = [];
        private int committed;

        public async ValueTask<GeneratedWordSearchTopic> PublishTopicAsync(
            WordSearchTopicArtifactSet topicSet,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(topicSet);
            cancellationToken.ThrowIfCancellationRequested();
            var artifactByKind = ValidateArtifacts(topicSet);
            var relativeTopicDirectory = Path.Combine("topics", topicSet.Topic.Index.ToString("000"));
            var topicDirectory = Path.Combine(cacheRoot, relativeTopicDirectory);

            try
            {
                Directory.CreateDirectory(topicDirectory);
                var publishedArtifacts = new List<WordSearchArtifact>(artifactByKind.Count);
                var manifestArtifactPaths = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var kind in Enum.GetValues<WordSearchArtifactKind>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var rendered = artifactByKind[kind];
                    var fileName = FileName(kind);
                    var relativePath = Path.Combine(relativeTopicDirectory, fileName).Replace('\\', '/');
                    await File.WriteAllBytesAsync(Path.Combine(topicDirectory, fileName), rendered.Content, cancellationToken);
                    publishedArtifacts.Add(new WordSearchArtifact(kind, relativePath, rendered.Width, rendered.Height));
                    manifestArtifactPaths.Add(ManifestName(kind), relativePath);
                }

                var manifestTopic = new ManifestTopic(
                    topicSet.Topic.Index,
                    topicSet.Topic.Name,
                    topicSet.Topic.Quote,
                    topicSet.Topic.Entries.Select(entry => new ManifestEntry(
                        entry.SourceRow,
                        entry.Keyword,
                        entry.WordSearchKey)).ToArray(),
                    manifestArtifactPaths,
                    topicSet.Placements.Select(placement => new ManifestPlacement(
                        placement.WordSearchKey,
                        placement.Cells.Select(cell => new ManifestCell(cell.X, cell.Y)).ToArray())).ToArray());
                if (!manifestTopics.TryAdd(topicSet.Topic.Index, manifestTopic))
                {
                    throw new WordSearchGenerationException(
                        "topic_work_key_duplicate",
                        $"Topic work key 'topic:{topicSet.Topic.Index}' is duplicated.");
                }

                return new GeneratedWordSearchTopic(
                    topicSet.Topic.Index,
                    topicSet.Topic.Name,
                    publishedArtifacts,
                    topicSet.Placements);
            }
            catch (WordSearchGenerationException)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new WordSearchGenerationException(
                    "cache_topic_publish_failed",
                    $"Topic {topicSet.Topic.Index} '{topicSet.Topic.Name}' could not be written to cache: {exception.Message}",
                    exception);
            }
        }

        public async ValueTask<WordSearchGenerationResult> CommitAsync(
            IReadOnlyList<GeneratedWordSearchTopic> topics,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(topics);
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Exchange(ref committed, 1) != 0)
            {
                throw new InvalidOperationException("The cache session has already been committed.");
            }

            var orderedTopics = topics.OrderBy(topic => topic.Index).ToArray();
            if (orderedTopics.Length != manifestTopics.Count ||
                orderedTopics.Any(topic => !manifestTopics.ContainsKey(topic.Index)))
            {
                throw new WordSearchGenerationException(
                    "cache_manifest_incomplete",
                    "The cache manifest does not contain every processed Topic.");
            }

            var manifestPath = Path.Combine(cacheRoot, "manifest.json");
            var pendingManifestPath = Path.Combine(cacheRoot, "manifest.pending.json");
            try
            {
                RemoveStaleTopicDirectories(orderedTopics.Select(topic => topic.Index).ToHashSet());
                RemoveLegacyBrandCaches();
                RemoveUnknownCacheFiles();
                var manifest = new ManifestDocument(
                    ManifestSchemaVersion,
                    request.BookId,
                    request.BrandId,
                    settings.Global.Board,
                    settings.Global.Page,
                    orderedTopics.Select(topic => manifestTopics[topic.Index]).ToArray());
                await using (var stream = new FileStream(
                                 pendingManifestPath,
                                 FileMode.Create,
                                 FileAccess.Write,
                                 FileShare.None,
                                 64 * 1024,
                                 FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();
                File.Move(pendingManifestPath, manifestPath, overwrite: true);
                return new WordSearchGenerationResult(
                    request.BookId,
                    request.BrandId,
                    manifestPath,
                    orderedTopics);
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
                throw new WordSearchGenerationException(
                    "cache_publish_failed",
                    $"Word search cache manifest could not be published: {exception.Message}",
                    exception);
            }
            finally
            {
                TryDeleteFile(pendingManifestPath);
            }
        }

        public ValueTask DisposeAsync()
        {
            TryDeleteFile(Path.Combine(cacheRoot, "manifest.pending.json"));
            return ValueTask.CompletedTask;
        }

        private void RemoveStaleTopicDirectories(IReadOnlySet<int> currentTopicIndexes)
        {
            var topicsRoot = Path.Combine(cacheRoot, "topics");
            foreach (var directory in Directory.EnumerateDirectories(topicsRoot))
            {
                if (!int.TryParse(Path.GetFileName(directory), out var index) || !currentTopicIndexes.Contains(index))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        private void RemoveLegacyBrandCaches()
        {
            foreach (var directory in Directory.EnumerateDirectories(cacheRoot))
            {
                if (!string.Equals(Path.GetFileName(directory), "topics", StringComparison.OrdinalIgnoreCase))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        private void RemoveUnknownCacheFiles()
        {
            foreach (var file in Directory.EnumerateFiles(cacheRoot))
            {
                if (!string.Equals(Path.GetFileName(file), "manifest.pending.json", StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(file);
                }
            }
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

    private static void DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            DeleteFile(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A later cache session will clean the stable pending file.
        }
    }

    private static string FileName(WordSearchArtifactKind kind) => kind switch
    {
        WordSearchArtifactKind.BoardGame => "board-game.png",
        WordSearchArtifactKind.BoardGameAnswer => "board-game-answer.png",
        WordSearchArtifactKind.Page => "page.png",
        WordSearchArtifactKind.PageAnswer => "page-answer.png",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    private static string ManifestName(WordSearchArtifactKind kind) => kind switch
    {
        WordSearchArtifactKind.BoardGame => "boardGame",
        WordSearchArtifactKind.BoardGameAnswer => "boardGameAnswer",
        WordSearchArtifactKind.Page => "page",
        WordSearchArtifactKind.PageAnswer => "pageAnswer",
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
        string Quote,
        IReadOnlyList<ManifestEntry> Entries,
        IReadOnlyDictionary<string, string> Artifacts,
        IReadOnlyList<ManifestPlacement> Placements);

    private sealed record ManifestEntry(int SourceRow, string Keyword, string WordSearchKey);

    private sealed record ManifestPlacement(string WordSearchKey, IReadOnlyList<ManifestCell> Cells);

    private sealed record ManifestCell(int X, int Y);
}
