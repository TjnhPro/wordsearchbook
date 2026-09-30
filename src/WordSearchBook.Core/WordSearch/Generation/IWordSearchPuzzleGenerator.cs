using WordSearchBook.Core.WordSearch.Domain;

namespace WordSearchBook.Core.WordSearch.Generation;

public interface IWordSearchPuzzleGenerator
{
    WordSearchPuzzle Generate(IReadOnlyList<string> wordSearchKeys, BoardSize boardSize);
}
