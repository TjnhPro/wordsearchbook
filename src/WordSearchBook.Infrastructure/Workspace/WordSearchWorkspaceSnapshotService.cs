using System.Text.Json;
using WordSearchBook.Core.Application.Workspace;
using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Core.WordSearch.Domain;
using WordSearchBook.Core.WordSearch.Input;
using WordSearchBook.Core.WordSearch.Settings;
using WordSearchBook.Core.WordSearch.Validation;
using WordSearchBook.Infrastructure.WordSearch.Validation;

namespace WordSearchBook.Infrastructure.Workspace;

public sealed class WordSearchWorkspaceSnapshotService(
    IBookDataValidationService bookDataValidationService,
    IWordSearchSettingsReader settingsReader,
    IBrandValidationService validationService,
    IBookBrandAssignmentStore assignmentStore,
    IBookOutputSnapshotService outputSnapshotService) : IWorkspaceSnapshotService
{
    public async Task<WorkspaceSnapshot> RefreshAsync(
        string rootPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        var fullRoot = Path.GetFullPath(rootPath);
        if (!Directory.Exists(fullRoot))
        {
            throw new WordSearchGenerationException("root_not_found", "The application root directory was not found.");
        }

        var assignments = await assignmentStore.ReadAsync(cancellationToken);
        var (global, globalIssue) = await ReadGlobalAsync(fullRoot, cancellationToken);
        var brands = await ReadBrandsAsync(fullRoot, global, globalIssue, cancellationToken);
        var books = await ReadBooksAsync(fullRoot, assignments, brands, global, cancellationToken);
        return new WorkspaceSnapshot(fullRoot, global, globalIssue, brands, books, DateTimeOffset.UtcNow);
    }

    private async Task<(GlobalWordSearchSettings? Settings, WorkspaceIssue? Issue)> ReadGlobalAsync(
        string rootPath,
        CancellationToken cancellationToken)
    {
        try
        {
            return (await settingsReader.ReadGlobalAsync(rootPath, cancellationToken), null);
        }
        catch (WordSearchGenerationException exception)
        {
            return (null, Issue(exception));
        }
    }

    private async Task<IReadOnlyList<WorkspaceBrand>> ReadBrandsAsync(
        string rootPath,
        GlobalWordSearchSettings? global,
        WorkspaceIssue? globalIssue,
        CancellationToken cancellationToken)
    {
        var brandsRoot = Path.Combine(rootPath, "brands");
        if (!Directory.Exists(brandsRoot))
        {
            return [];
        }

        var results = new List<WorkspaceBrand>();
        foreach (var directory in Directory.EnumerateDirectories(brandsRoot).Order(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var brandId = Path.GetFileName(directory);
            var validation = await validationService.CheckStateAsync(rootPath, brandId, cancellationToken);
            var (assetFolders, assetIssue) = ReadAssetFolders(rootPath, brandId, validation.Status);
            var qrPage = ReadQrPage(rootPath, brandId, validation.Status);
            if (global is null)
            {
                results.Add(new WorkspaceBrand(
                    brandId,
                    null,
                    validation,
                    globalIssue ?? assetIssue,
                    assetFolders,
                    QrPage: qrPage));
                continue;
            }

            try
            {
                var settings = await settingsReader.ReadBrandAsync(rootPath, brandId, global, cancellationToken);
                results.Add(new WorkspaceBrand(
                    brandId,
                    settings.Settings,
                    validation,
                    assetIssue,
                    assetFolders,
                    settings.RequiresSave,
                    settings.UpdateReasonCode,
                    qrPage));
            }
            catch (WordSearchGenerationException exception)
            {
                results.Add(new WorkspaceBrand(
                    brandId,
                    null,
                    validation,
                    Issue(exception),
                    assetFolders,
                    QrPage: qrPage));
            }
        }

        return results;
    }

    private static WorkspaceBrandOptionalFile ReadQrPage(
        string rootPath,
        string brandId,
        BrandValidationStatus status)
    {
        var path = BrandAssetDiscovery.ResolveQrPagePath(rootPath, brandId);
        return new WorkspaceBrandOptionalFile(
            BrandValidationDefinition.QrPageKey,
            BrandValidationDefinition.QrPageRelativePath,
            File.Exists(path),
            ".png",
            status);
    }

    private static (IReadOnlyList<WorkspaceBrandAssetFolder> Folders, WorkspaceIssue? Issue) ReadAssetFolders(
        string rootPath,
        string brandId,
        BrandValidationStatus status)
    {
        try
        {
            var folders = BrandAssetDiscovery.DiscoverOptionalFolders(rootPath, brandId)
                .Select(folder => new WorkspaceBrandAssetFolder(
                    folder.Key,
                    folder.RelativePath,
                    folder.Exists,
                    folder.Files.Select(file => new WorkspaceBrandAssetFile(
                        file.Name,
                        file.RelativePath,
                        file.Extension,
                        status)).ToArray()))
                .ToArray();
            return (folders, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            var empty = new[]
            {
                new WorkspaceBrandAssetFolder(
                    BrandValidationDefinition.FrontKey,
                    BrandValidationDefinition.FrontRelativePath,
                    Exists: false,
                    []),
                new WorkspaceBrandAssetFolder(
                    BrandValidationDefinition.BackKey,
                    BrandValidationDefinition.BackRelativePath,
                    Exists: false,
                    [])
            };
            return (empty, new WorkspaceIssue("brand_assets_unavailable", $"Brand assets could not be read: {exception.Message}"));
        }
    }

    private async Task<IReadOnlyList<WorkspaceBook>> ReadBooksAsync(
        string rootPath,
        IReadOnlyDictionary<string, string> assignments,
        IReadOnlyList<WorkspaceBrand> brands,
        GlobalWordSearchSettings? global,
        CancellationToken cancellationToken)
    {
        var inputRoot = Path.Combine(rootPath, "input");
        if (!Directory.Exists(inputRoot))
        {
            return [];
        }

        var knownBrands = brands.Select(brand => brand.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var brandValidations = brands.ToDictionary(
            brand => brand.Id,
            brand => brand.Validation,
            StringComparer.OrdinalIgnoreCase);
        var results = new List<WorkspaceBook>();
        foreach (var directory in Directory.EnumerateDirectories(inputRoot).Order(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bookId = Path.GetFileName(directory);
            assignments.TryGetValue(bookId, out var selectedBrandId);
            if (selectedBrandId is not null && !knownBrands.Contains(selectedBrandId))
            {
                selectedBrandId = null;
            }

            var cachedBrands = ReadCachedBrands(directory);
            var validation = await bookDataValidationService.CheckStateAsync(
                rootPath,
                bookId,
                global?.MaximumKeywordLength ?? WordSearchSettingsDefaults.MaximumKeywordLength,
                cancellationToken);
            var output = await outputSnapshotService.ReadAsync(
                rootPath,
                bookId,
                selectedBrandId,
                validation,
                brandValidations,
                cancellationToken);
            results.Add(new WorkspaceBook(
                bookId,
                validation.TopicCount,
                selectedBrandId,
                cachedBrands,
                DataIssue(validation),
                validation,
                output));
        }

        return results;
    }

    private static IReadOnlyList<string> ReadCachedBrands(string bookDirectory)
    {
        var cacheRoot = Path.Combine(bookDirectory, ".workspace", "cache");
        var manifestPath = Path.Combine(cacheRoot, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return [];
        }

        try
        {
            using var manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
            var brandId = manifest.RootElement.GetProperty("brandId").GetString();
            return string.IsNullOrWhiteSpace(brandId) ? [] : [brandId];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return [];
        }
    }

    private static WorkspaceIssue Issue(WordSearchGenerationException exception) => new(exception.Code, exception.Message);

    private static WorkspaceIssue? DataIssue(BookDataValidationState validation) => validation.Status switch
    {
        BookDataValidationStatus.Validated => null,
        BookDataValidationStatus.Invalid when validation.Failures?.FirstOrDefault() is { } failure =>
            new WorkspaceIssue(failure.Code, failure.Message),
        BookDataValidationStatus.Invalid =>
            new WorkspaceIssue("book_data_invalid", "data.csv is invalid."),
        BookDataValidationStatus.NeedsValidation =>
            new WorkspaceIssue(
                validation.ReasonCode ?? "book_data_needs_validation",
                "data.csv changed and must be validated again."),
        _ => new WorkspaceIssue("book_data_not_validated", "data.csv has not been validated.")
    };
}
