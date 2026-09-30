using WordSearchBook.Core.WordSearch.Contracts;

namespace WordSearchBook.Core.WordSearch.Application;

public interface IWordSearchBookGenerationService
{
    Task<WordSearchGenerationResult> GenerateAsync(
        WordSearchGenerationRequest request,
        CancellationToken cancellationToken = default);
}
