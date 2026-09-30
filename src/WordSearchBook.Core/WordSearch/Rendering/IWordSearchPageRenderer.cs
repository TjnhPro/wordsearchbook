using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Core.WordSearch.Domain;

namespace WordSearchBook.Core.WordSearch.Rendering;

public interface IWordSearchPageRenderer
{
    RenderedWordSearchArtifact Render(
        string pageLayoutPath,
        WordSearchTopic topic,
        int pageNumber,
        RenderedWordSearchArtifact boardArtifact,
        WordSearchSettingsBundle settings,
        WordSearchArtifactKind outputKind);
}
