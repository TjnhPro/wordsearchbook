using WordSearchBook.Core.Application.Workspace;
using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Core.WordSearch.Domain;
using WordSearchBook.Core.WordSearch.Input;
using WordSearchBook.Core.WordSearch.Settings;

namespace WordSearchBook.Infrastructure.Workspace;

public sealed class WordSearchWorkspaceSnapshotService(
    IWordSearchInputReader inputReader,
    IWordSearchSettingsReader settingsReader,
    IBookBrandAssignmentStore assignmentStore) : IWorkspaceSnapshotService
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
        var books = await ReadBooksAsync(fullRoot, assignments, brands, cancellationToken);
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
            if (global is null)
            {
                results.Add(new WorkspaceBrand(brandId, null, globalIssue));
                continue;
            }

            try
            {
                var settings = await settingsReader.ReadBrandAsync(rootPath, brandId, global, cancellationToken);
                results.Add(new WorkspaceBrand(brandId, settings, null));
            }
            catch (WordSearchGenerationException exception)
            {
                results.Add(new WorkspaceBrand(brandId, null, Issue(exception)));
            }
        }

        return results;
    }

    private async Task<IReadOnlyList<WorkspaceBook>> ReadBooksAsync(
        string rootPath,
        IReadOnlyDictionary<string, string> assignments,
        IReadOnlyList<WorkspaceBrand> brands,
        CancellationToken cancellationToken)
    {
        var inputRoot = Path.Combine(rootPath, "input");
        if (!Directory.Exists(inputRoot))
        {
            return [];
        }

        var knownBrands = brands.Select(brand => brand.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
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
            try
            {
                var topics = await inputReader.ReadAsync(Path.Combine(directory, "data.csv"), cancellationToken);
                results.Add(new WorkspaceBook(bookId, topics.Count, selectedBrandId, cachedBrands, null));
            }
            catch (WordSearchGenerationException exception)
            {
                results.Add(new WorkspaceBook(bookId, 0, selectedBrandId, cachedBrands, Issue(exception)));
            }
        }

        return results;
    }

    private static IReadOnlyList<string> ReadCachedBrands(string bookDirectory)
    {
        var cacheRoot = Path.Combine(bookDirectory, ".workspace", "cache");
        if (!Directory.Exists(cacheRoot))
        {
            return [];
        }

        return Directory.EnumerateDirectories(cacheRoot)
            .Where(directory => File.Exists(Path.Combine(directory, "manifest.json")))
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static WorkspaceIssue Issue(WordSearchGenerationException exception) => new(exception.Code, exception.Message);
}
