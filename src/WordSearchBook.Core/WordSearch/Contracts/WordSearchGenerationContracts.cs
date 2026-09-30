using WordSearchBook.Core.WordSearch.Domain;

namespace WordSearchBook.Core.WordSearch.Contracts;

public enum WordSearchArtifactKind
{
    Topic,
    BoardGame,
    BoardGameAnswer,
    KeywordList,
    PageNumber
}

public sealed record WordSearchGenerationRequest(
    string RootPath,
    string BookId,
    string BrandId);

public sealed record WordSearchArtifact(
    WordSearchArtifactKind Kind,
    string RelativePath,
    int Width,
    int Height);

public sealed record RenderedWordSearchArtifact(
    WordSearchArtifactKind Kind,
    byte[] Content,
    int Width,
    int Height,
    string MediaType = "image/png");

public sealed record GeneratedWordSearchTopic(
    int Index,
    string Name,
    IReadOnlyList<WordSearchArtifact> Artifacts,
    IReadOnlyList<WordPlacement> Placements);

public sealed record WordSearchGenerationResult(
    string BookId,
    string BrandId,
    string ManifestPath,
    IReadOnlyList<GeneratedWordSearchTopic> Topics);
