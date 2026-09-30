using WordSearchBook.Core.WordSearch.Domain;

namespace WordSearchBook.Core.WordSearch.Settings;

public interface IWordSearchSettingsReader
{
    Task<GlobalWordSearchSettings> ReadGlobalAsync(
        string rootPath,
        CancellationToken cancellationToken = default);

    Task<BrandWordSearchSettings> ReadBrandAsync(
        string rootPath,
        string brandId,
        GlobalWordSearchSettings global,
        CancellationToken cancellationToken = default);

    Task<WordSearchSettingsBundle> ReadAsync(
        string rootPath,
        string brandId,
        CancellationToken cancellationToken = default);
}
