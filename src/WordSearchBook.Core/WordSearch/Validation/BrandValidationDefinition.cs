using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace WordSearchBook.Core.WordSearch.Validation;

public static class BrandValidationDefinition
{
    public const int SchemaVersion = 1;
    public const int AssetFingerprintFormatVersion = 1;
    public const string EntryKey = "page-layout";
    public const string PageLayoutRelativePath = "page_layout.png";
    public const int PageWidth = 2588;
    public const int PageHeight = 3375;

    public static readonly DateTimeOffset ChangedAtUtc =
        new(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);

    public static readonly IReadOnlyList<string> Rules =
    [
        $"dimensions:{PageWidth}x{PageHeight}",
        "exists",
        "format:png",
        "readable"
    ];

    public static string Signature { get; } = CalculateSignature(Rules);

    public static string CalculateSignature(IEnumerable<string> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var normalizedRules = rules
            .Select(rule => string.IsNullOrWhiteSpace(rule)
                ? throw new ArgumentException("Definition rules cannot be empty.", nameof(rules))
                : rule.Trim())
            .Order(StringComparer.Ordinal);
        var manifest = string.Join('\n',
        [
            $"definitionFormatVersion={SchemaVersion.ToString(CultureInfo.InvariantCulture)}",
            $"definitionChangedAtUtc={ChangedAtUtc:O}",
            $"entry={EntryKey}",
            $"targetKind=file|path={NormalizeRelativePath(PageLayoutRelativePath)}",
            .. normalizedRules.Select(rule => $"rule={rule}")
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
            $"entry={BrandValidationDefinition.EntryKey}",
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

