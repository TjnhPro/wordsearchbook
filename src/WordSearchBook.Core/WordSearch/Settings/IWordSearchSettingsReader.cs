using WordSearchBook.Core.WordSearch.Domain;

namespace WordSearchBook.Core.WordSearch.Settings;

public interface IWordSearchSettingsReader
{
    Task<GlobalWordSearchSettings> ReadGlobalAsync(
        string rootPath,
        CancellationToken cancellationToken = default);

    Task<BrandSettingsReadResult> ReadBrandAsync(
        string rootPath,
        string brandId,
        GlobalWordSearchSettings global,
        CancellationToken cancellationToken = default);

    Task<WordSearchSettingsBundle> ReadAsync(
        string rootPath,
        string brandId,
        CancellationToken cancellationToken = default);
}

public sealed record BrandSettingsReadResult(
    BrandWordSearchSettings Settings,
    bool RequiresSave,
    string? UpdateReasonCode = null);

public interface IWordSearchSettingsWriter
{
    Task CreateBrandAsync(
        string rootPath,
        string brandId,
        CancellationToken cancellationToken = default);

    Task SaveGlobalAsync(
        string rootPath,
        GlobalWordSearchSettings settings,
        CancellationToken cancellationToken = default);

    Task SaveBrandAsync(
        string rootPath,
        string brandId,
        BrandWordSearchSettings settings,
        CancellationToken cancellationToken = default);
}
