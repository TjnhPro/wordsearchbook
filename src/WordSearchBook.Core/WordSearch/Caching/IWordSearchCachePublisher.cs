using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Core.WordSearch.Domain;

namespace WordSearchBook.Core.WordSearch.Caching;

public sealed record WordSearchTopicArtifactSet(
    WordSearchTopic Topic,
    IReadOnlyList<RenderedWordSearchArtifact> Artifacts,
    IReadOnlyList<WordPlacement> Placements);

public interface IWordSearchCachePublisher
{
    Task<WordSearchGenerationResult> PublishAsync(
        WordSearchGenerationRequest request,
        WordSearchSettingsBundle settings,
        IReadOnlyList<WordSearchTopicArtifactSet> topics,
        CancellationToken cancellationToken = default);
}
