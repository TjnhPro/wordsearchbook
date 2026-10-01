using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace WordSearchBook.Core.WordSearch.Validation;

public enum BrandValidationTargetKind
{
    File,
    Directory
}

public sealed record BrandValidationEntryDefinition(
    string Key,
    BrandValidationTargetKind TargetKind,
    string RelativePath,
    bool Required,
    bool Recursive,
    IReadOnlyList<string> Extensions,
    IReadOnlyList<string> Rules);

public static class BrandValidationDefinition
{
    public const int SchemaVersion = 3;
    public const int AssetFingerprintFormatVersion = 2;
    public const string PageLayoutKey = "page-layout";
    public const string FrontLayoutKey = "front-layout";
    public const string FrontKey = "front";
    public const string BackKey = "back";
    public const string PageLayoutRelativePath = "page_layout.png";
    public const string FrontLayoutRelativePath = "front_layout.png";
    public const string FrontRelativePath = "front";
    public const string BackRelativePath = "back";
    public const int PageWidth = 2588;
    public const int PageHeight = 3375;

    public static readonly DateTimeOffset ChangedAtUtc =
        new(2026, 10, 1, 5, 0, 0, TimeSpan.Zero);

    public static readonly IReadOnlyList<string> SupportedImageExtensions =
        [".jpeg", ".jpg", ".png"];

    public static readonly IReadOnlyList<BrandValidationEntryDefinition> Entries =
    [
        new(
            PageLayoutKey,
            BrandValidationTargetKind.File,
            PageLayoutRelativePath,
            Required: true,
            Recursive: false,
            [".png"],
            [$"dimensions:{PageWidth}x{PageHeight}", "exists", "format:png", "readable"]),
        new(
            FrontLayoutKey,
            BrandValidationTargetKind.File,
            FrontLayoutRelativePath,
            Required: true,
            Recursive: false,
            [".png"],
            [$"dimensions:{PageWidth}x{PageHeight}", "exists", "format:png", "readable"]),
        new(
            FrontKey,
            BrandValidationTargetKind.Directory,
            FrontRelativePath,
            Required: false,
            Recursive: false,
            SupportedImageExtensions,
            [$"dimensions:{PageWidth}x{PageHeight}", "format:jpeg|png", "readable"]),
        new(
            BackKey,
            BrandValidationTargetKind.Directory,
            BackRelativePath,
            Required: false,
            Recursive: false,
            SupportedImageExtensions,
            [$"dimensions:{PageWidth}x{PageHeight}", "format:jpeg|png", "readable"])
    ];

    public static string Signature { get; } = CalculateSignature(Entries);

    public static string CalculateSignature(IEnumerable<BrandValidationEntryDefinition> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var entryLines = entries
            .Select(BuildEntryManifest)
            .Order(StringComparer.Ordinal);
        var manifest = string.Join('\n',
        [
            $"definitionFormatVersion={SchemaVersion.ToString(CultureInfo.InvariantCulture)}",
            $"definitionChangedAtUtc={ChangedAtUtc:O}",
            .. entryLines
        ]);
        return Hash(manifest);
    }

    public static string NormalizeRelativePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (Path.IsPathRooted(path))
        {
            throw new ArgumentException("Validation asset paths must be relative.", nameof(path));
        }

        var segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
        {
            throw new ArgumentException("Validation asset paths cannot contain traversal segments.", nameof(path));
        }

        return string.Join('/', segments).ToLowerInvariant();
    }

    internal static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"sha256:{Convert.ToHexStringLower(bytes)}";
    }

    private static string BuildEntryManifest(BrandValidationEntryDefinition entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (string.IsNullOrWhiteSpace(entry.Key))
        {
            throw new ArgumentException("Validation entry keys cannot be empty.", nameof(entry));
        }

        var extensions = entry.Extensions
            .Select(extension => string.IsNullOrWhiteSpace(extension)
                ? throw new ArgumentException("Validation extensions cannot be empty.", nameof(entry))
                : extension.Trim().ToLowerInvariant())
            .Order(StringComparer.Ordinal);
        var rules = entry.Rules
            .Select(rule => string.IsNullOrWhiteSpace(rule)
                ? throw new ArgumentException("Definition rules cannot be empty.", nameof(entry))
                : rule.Trim())
            .Order(StringComparer.Ordinal);
        return string.Join('|',
        [
            $"entry={entry.Key.Trim().ToLowerInvariant()}",
            $"targetKind={entry.TargetKind.ToString().ToLowerInvariant()}",
            $"path={NormalizeRelativePath(entry.RelativePath)}",
            $"required={entry.Required.ToString(CultureInfo.InvariantCulture).ToLowerInvariant()}",
            $"recursive={entry.Recursive.ToString(CultureInfo.InvariantCulture).ToLowerInvariant()}",
            $"extensions={string.Join(',', extensions)}",
            $"rules={string.Join(',', rules)}"
        ]);
    }
}

public sealed record BrandValidationFileMetadata(
    string RelativePath,
    long? LengthBytes,
    DateTimeOffset? LastWriteTimeUtc)
{
    public static BrandValidationFileMetadata Missing(string relativePath) => new(relativePath, null, null);
}

public static class BrandAssetFingerprintCalculator
{
    public static string Calculate(IEnumerable<BrandValidationFileMetadata> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var lines = files
            .Select(BuildFileLine)
            .Order(StringComparer.Ordinal);
        var manifest = string.Join('\n',
        [
            $"assetFingerprintFormatVersion={BrandValidationDefinition.AssetFingerprintFormatVersion.ToString(CultureInfo.InvariantCulture)}",
            .. lines
        ]);
        return BrandValidationDefinition.Hash(manifest);
    }

    private static string BuildFileLine(BrandValidationFileMetadata file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var path = BrandValidationDefinition.NormalizeRelativePath(file.RelativePath);
        if (file.LengthBytes is null || file.LastWriteTimeUtc is null)
        {
            return $"file={path}|missing";
        }

        if (file.LengthBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(file), "File length cannot be negative.");
        }

        return $"file={path}|length={file.LengthBytes.Value.ToString(CultureInfo.InvariantCulture)}|lastWriteUtcTicks={file.LastWriteTimeUtc.Value.UtcTicks.ToString(CultureInfo.InvariantCulture)}";
    }
}
