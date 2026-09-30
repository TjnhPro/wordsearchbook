using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Core.WordSearch.Domain;

namespace WordSearchBook.Core.WordSearch.Rendering;

public interface IWordSearchTextArtifactRenderer
{
    RenderedWordSearchArtifact RenderTopic(string topic, TextRegionSettings style);

    RenderedWordSearchArtifact RenderKeywordList(IReadOnlyList<string> keywords, TextRegionSettings style);

    RenderedWordSearchArtifact RenderPageNumber(int topicIndex, TextRegionSettings style);
}
