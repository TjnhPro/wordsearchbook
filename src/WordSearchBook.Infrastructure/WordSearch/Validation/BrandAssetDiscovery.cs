using WordSearchBook.Core.WordSearch.Validation;

namespace WordSearchBook.Infrastructure.WordSearch.Validation;

internal sealed record DiscoveredBrandAssetFile(
    string EntryKey,
    string Name,
    string RelativePath,
    string FullPath,
    string Extension);

internal sealed record DiscoveredBrandAssetFolder(
    string Key,
    string RelativePath,
    bool Exists,
    IReadOnlyList<DiscoveredBrandAssetFile> Files);

internal static class BrandAssetDiscovery
{
    private static readonly HashSet<string> SupportedExtensions =
        new(BrandValidationDefinition.SupportedImageExtensions, StringComparer.OrdinalIgnoreCase);

    public static string ResolveBrandDirectory(string rootPath, string brandId)
    {
        var certificatePath = JsonBrandValidationStateStore.ResolvePath(rootPath, brandId);
        return Path.GetDirectoryName(certificatePath)!;
    }

    public static string ResolveLayoutPath(string rootPath, string brandId) =>
        Path.Combine(ResolveBrandDirectory(rootPath, brandId), BrandValidationDefinition.PageLayoutRelativePath);

    public static string ResolveFrontLayoutPath(string rootPath, string brandId) =>
        Path.Combine(ResolveBrandDirectory(rootPath, brandId), BrandValidationDefinition.FrontLayoutRelativePath);

    public static string ResolveQrPagePath(string rootPath, string brandId) =>
        Path.Combine(ResolveBrandDirectory(rootPath, brandId), BrandValidationDefinition.QrPageRelativePath);

    public static IReadOnlyList<DiscoveredBrandAssetFolder> DiscoverOptionalFolders(
        string rootPath,
        string brandId)
    {
        var brandDirectory = ResolveBrandDirectory(rootPath, brandId);
        return
        [
            DiscoverFolder(brandDirectory, BrandValidationDefinition.FrontKey, BrandValidationDefinition.FrontRelativePath),
            DiscoverFolder(brandDirectory, BrandValidationDefinition.BackKey, BrandValidationDefinition.BackRelativePath)
        ];
    }

    public static IReadOnlyList<DiscoveredBrandAssetFile> DiscoverTrackedFiles(
        string rootPath,
        string brandId) =>
        DiscoverOptionalFolders(rootPath, brandId)
            .SelectMany(folder => folder.Files)
            .ToArray();

    public static IReadOnlyList<BrandValidationFileMetadata> CaptureMetadata(
        string rootPath,
        string brandId)
    {
        var layoutPath = ResolveLayoutPath(rootPath, brandId);
        var layout = new FileInfo(layoutPath);
        layout.Refresh();
        var frontLayoutPath = ResolveFrontLayoutPath(rootPath, brandId);
        var frontLayout = new FileInfo(frontLayoutPath);
        frontLayout.Refresh();
        var qrPagePath = ResolveQrPagePath(rootPath, brandId);
        var qrPage = new FileInfo(qrPagePath);
        qrPage.Refresh();
        var metadata = new List<BrandValidationFileMetadata>
        {
            layout.Exists
                ? CreateMetadata(BrandValidationDefinition.PageLayoutRelativePath, layout)
                : BrandValidationFileMetadata.Missing(BrandValidationDefinition.PageLayoutRelativePath),
            frontLayout.Exists
                ? CreateMetadata(BrandValidationDefinition.FrontLayoutRelativePath, frontLayout)
                : BrandValidationFileMetadata.Missing(BrandValidationDefinition.FrontLayoutRelativePath),
            qrPage.Exists
                ? CreateMetadata(BrandValidationDefinition.QrPageRelativePath, qrPage)
                : BrandValidationFileMetadata.Missing(BrandValidationDefinition.QrPageRelativePath)
        };

        foreach (var asset in DiscoverTrackedFiles(rootPath, brandId))
        {
            var file = new FileInfo(asset.FullPath);
            file.Refresh();
            if (file.Exists)
            {
                metadata.Add(CreateMetadata(asset.RelativePath, file));
            }
        }

        return metadata;
    }

    private static DiscoveredBrandAssetFolder DiscoverFolder(
        string brandDirectory,
        string key,
        string relativePath)
    {
        var fullPath = Path.Combine(brandDirectory, relativePath);
        if (!Directory.Exists(fullPath))
        {
            return new DiscoveredBrandAssetFolder(key, relativePath, Exists: false, []);
        }

        var files = Directory.EnumerateFiles(fullPath, "*", SearchOption.TopDirectoryOnly)
            .Where(path => SupportedExtensions.Contains(Path.GetExtension(path)))
            .Select(path => new DiscoveredBrandAssetFile(
                key,
                Path.GetFileName(path),
                BrandValidationDefinition.NormalizeRelativePath(Path.Combine(relativePath, Path.GetFileName(path))),
                path,
                Path.GetExtension(path).ToLowerInvariant()))
            .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.Name, StringComparer.Ordinal)
            .ToArray();
        return new DiscoveredBrandAssetFolder(key, relativePath, Exists: true, files);
    }

    private static BrandValidationFileMetadata CreateMetadata(string relativePath, FileInfo file) =>
        new(relativePath, file.Length, new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero));
}
