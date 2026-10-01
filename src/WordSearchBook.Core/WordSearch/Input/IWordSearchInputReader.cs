using WordSearchBook.Core.WordSearch.Domain;

namespace WordSearchBook.Core.WordSearch.Input;

public interface IWordSearchInputReader
{
    Task<IReadOnlyList<WordSearchTopic>> ReadAsync(
        string dataCsvPath,
        int maximumKeywordLength = WordSearchSettingsDefaults.MaximumKeywordLength,
        CancellationToken cancellationToken = default);
}
