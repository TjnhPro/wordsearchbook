using System.Text.Json;
using System.Text.Json.Serialization;
using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Core.WordSearch.Validation;

namespace WordSearchBook.Infrastructure.WordSearch.Validation;

public sealed class JsonBrandValidationStateStore : IBrandValidationStateStore
{
    internal const string FileName = "brand.validation.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true
    };

    public async ValueTask<BrandValidationRecord?> LoadAsync(
        string rootPath,
        string brandId,
        CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(rootPath, brandId);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<BrandValidationRecord>(stream, JsonOptions, cancellationToken)
                ?? throw Unavailable(path);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (WordSearchGenerationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            throw Unavailable(path, exception);
        }
    }

    public async ValueTask SaveAsync(
        string rootPath,
        string brandId,
        BrandValidationRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        var path = ResolvePath(rootPath, brandId);
        var directory = Path.GetDirectoryName(path)!;
        var temporaryPath = Path.Combine(directory, $".{FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, record, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new WordSearchGenerationException(
                "brand_validation_state_save_failed",
                $"Brand validation state could not be saved: {path}",
                exception);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    internal static string ResolvePath(string rootPath, string brandId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ValidateBrandId(brandId);
        var root = Path.GetFullPath(rootPath);
        var brandDirectory = Path.GetFullPath(Path.Combine(root, "brands", brandId));
        var brandsDirectory = Path.GetFullPath(Path.Combine(root, "brands")) + Path.DirectorySeparatorChar;
        if (!brandDirectory.StartsWith(brandsDirectory, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(brandDirectory))
        {
            throw new WordSearchGenerationException("brand_not_found", $"Brand directory was not found: {brandId}");
        }

        return Path.Combine(brandDirectory, FileName);
    }

    internal static void ValidateBrandId(string brandId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(brandId);
        if (brandId is "." or ".." ||
            brandId.Contains('/') ||
            brandId.Contains('\\') ||
            brandId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new WordSearchGenerationException("brand_name_invalid", "Brand name must be a single safe folder name.");
        }
    }

    private static WordSearchGenerationException Unavailable(string path, Exception? innerException = null) =>
        new("brand_validation_state_unavailable", $"Brand validation state could not be read: {path}", innerException);
}
