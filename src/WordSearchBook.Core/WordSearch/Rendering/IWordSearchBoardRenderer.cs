using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Core.WordSearch.Domain;

namespace WordSearchBook.Core.WordSearch.Rendering;

public interface IWordSearchBoardRenderer
{
    RenderedWordSearchArtifact RenderData(
        WordSearchPuzzle puzzle,
        BoardSize boardSize,
        TextRegionSettings boardStyle);

    RenderedWordSearchArtifact RenderAnswer(
        WordSearchPuzzle puzzle,
        BoardSize boardSize,
        TextRegionSettings boardStyle,
        AnswerLineSettings answerLine);
}
