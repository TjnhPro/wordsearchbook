using WordSearchBook.Core.WordSearch.Domain;

namespace WordSearchBook.Core.WordSearch.Input;

public interface IWordSearchInputReader
{
    Task<IReadOnlyList<WordSearchTopic>> ReadAsync(
        string dataCsvPath,
        CancellationToken cancellationToken = default);
}
