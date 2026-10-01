using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Core.WordSearch.Domain;

namespace WordSearchBook.Core.WordSearch.Caching;

public sealed record WordSearchTopicArtifactSet(
    WordSearchTopic Topic,
    IReadOnlyList<RenderedWordSearchArtifact> Artifacts,
    IReadOnlyList<WordPlacement> Placements);

public interface IWordSearchCachePublisher
{
    Task<IWordSearchCacheSession> OpenAsync(
        WordSearchGenerationRequest request,
        WordSearchSettingsBundle settings,
        CancellationToken cancellationToken = default);
}

public interface IWordSearchCacheSession : IAsyncDisposable
{
    ValueTask<GeneratedWordSearchTopic> PublishTopicAsync(
        WordSearchTopicArtifactSet topic,
        CancellationToken cancellationToken = default);

    ValueTask<WordSearchGenerationResult> CommitAsync(
        IReadOnlyList<GeneratedWordSearchTopic> topics,
        CancellationToken cancellationToken = default);
}
