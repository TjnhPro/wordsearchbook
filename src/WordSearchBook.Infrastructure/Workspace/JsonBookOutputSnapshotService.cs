using System.Security.Cryptography;
using System.Text.Json;
using WordSearchBook.Core.Application.Workspace;
using WordSearchBook.Core.WordSearch.Application;
using WordSearchBook.Core.WordSearch.Validation;

namespace WordSearchBook.Infrastructure.Workspace;

public sealed class JsonBookOutputSnapshotService : IBookOutputSnapshotService
{
    private const int ManifestSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false
    };

    public async Task<WorkspaceBookOutput> ReadAsync(
        string rootPath,
        string bookId,
        string? selectedBrandId,
        BookDataValidationState dataValidation,
        IReadOnlyDictionary<string, BrandValidationState> brandValidations,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var bookDirectory = ResolveBookDirectory(rootPath, bookId);
            var manifestPath = Path.Combine(bookDirectory, ".workspace", "output.manifest.json");
            if (!File.Exists(manifestPath))
            {
                return new WorkspaceBookOutput(BookOutputStatus.Missing);
            }

            await using var stream = File.OpenRead(manifestPath);
            var manifest = await JsonSerializer.DeserializeAsync<OutputManifest>(stream, JsonOptions, cancellationToken)
                ?? throw new InvalidDataException("Output manifest is empty.");
            var summary = Summary(manifest, BookOutputStatus.Ready);
            if (manifest.SchemaVersion != ManifestSchemaVersion ||
                !string.Equals(manifest.BookId, bookId, StringComparison.Ordinal))
            {
                return summary with { Status = BookOutputStatus.Stale, ReasonCode = "output_manifest_outdated" };
            }

            if (dataValidation.Status != BookDataValidationStatus.Validated ||
                !string.Equals(dataValidation.ContentHash, manifest.DataContentHash, StringComparison.Ordinal))
            {
                return summary with { Status = BookOutputStatus.Stale, ReasonCode = "output_data_changed" };
            }

            if (!string.IsNullOrWhiteSpace(selectedBrandId) &&
                !string.Equals(selectedBrandId, manifest.BrandId, StringComparison.OrdinalIgnoreCase))
            {
                return summary with { Status = BookOutputStatus.Stale, ReasonCode = "output_brand_selection_changed" };
            }

            if (!brandValidations.TryGetValue(manifest.BrandId, out var brandValidation) ||
                brandValidation.Status != BrandValidationStatus.Validated ||
                !string.Equals(brandValidation.Fingerprint, manifest.BrandFingerprint, StringComparison.Ordinal))
            {
                return summary with { Status = BookOutputStatus.Stale, ReasonCode = "output_brand_changed" };
            }

            var settingsSignature = await CalculateSettingsSignatureAsync(rootPath, manifest.BrandId, cancellationToken);
            if (!string.Equals(settingsSignature, manifest.SettingsSignature, StringComparison.Ordinal))
            {
                return summary with { Status = BookOutputStatus.Stale, ReasonCode = "output_settings_changed" };
            }

            var outputDirectory = Path.Combine(bookDirectory, "output");
            var pdfPath = ResolveOutputPath(outputDirectory, manifest.Pdf.RelativePath);
            if (!File.Exists(pdfPath) || new FileInfo(pdfPath).Length != manifest.Pdf.LengthBytes)
            {
                return summary with { Status = BookOutputStatus.Stale, ReasonCode = "output_pdf_changed" };
            }

            foreach (var answer in manifest.Answers)
            {
                var answerPath = ResolveOutputPath(outputDirectory, answer.RelativePath);
                if (!File.Exists(answerPath) || new FileInfo(answerPath).Length != answer.LengthBytes)
                {
                    return summary with { Status = BookOutputStatus.Stale, ReasonCode = "output_answer_changed" };
                }
            }

            return summary;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException)
        {
            return new WorkspaceBookOutput(BookOutputStatus.Unavailable, ReasonCode: "output_state_unavailable");
        }
    }

    private static WorkspaceBookOutput Summary(OutputManifest manifest, BookOutputStatus status) => new(
        status,
        manifest.BrandId,
        manifest.Pdf.RelativePath,
        manifest.Pdf.LengthBytes,
        manifest.PuzzlePageCount,
        manifest.FrontPageCount,
        manifest.BackPageCount,
        manifest.Pdf.PageCount,
        manifest.Answers.Count,
        manifest.Answers.Sum(answer => answer.LengthBytes),
        manifest.ProcessedAtUtc);

    private static string ResolveBookDirectory(string rootPath, string bookId)
    {
        var inputDirectory = Path.GetFullPath(Path.Combine(rootPath, "input"));
        var bookDirectory = Path.GetFullPath(Path.Combine(inputDirectory, bookId));
        var prefix = inputDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!bookDirectory.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(bookDirectory))
        {
            throw new InvalidDataException("Book directory is unavailable.");
        }

        return bookDirectory;
    }

    private static string ResolveOutputPath(string outputDirectory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException("Output manifest contains an invalid path.");
        }

        var fullOutput = Path.GetFullPath(outputDirectory);
        var fullPath = Path.GetFullPath(Path.Combine(fullOutput, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = fullOutput.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Output manifest path escapes the output directory.");
        }

        return fullPath;
    }

    private static async Task<string> CalculateSettingsSignatureAsync(
        string rootPath,
        string brandId,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var path in new[]
                 {
                     Path.Combine(rootPath, "settings.json"),
                     Path.Combine(rootPath, "brands", brandId, "settings.json")
                 })
        {
            hash.AppendData(await File.ReadAllBytesAsync(path, cancellationToken));
            hash.AppendData([0]);
        }

        return $"sha256:{Convert.ToHexStringLower(hash.GetHashAndReset())}";
    }

    private sealed record PdfManifest(
        string RelativePath,
        long LengthBytes,
        int PageCount,
        int Width,
        int Height,
        int Dpi);

    private sealed record OutputManifest(
        int SchemaVersion,
        string BookId,
        string BrandId,
        string DataContentHash,
        string BrandFingerprint,
        string SettingsSignature,
        DateTimeOffset ProcessedAtUtc,
        int PuzzlePageCount,
        int FrontPageCount,
        int BackPageCount,
        PdfManifest Pdf,
        IReadOnlyList<BookAnswerOutput> Answers);
}
