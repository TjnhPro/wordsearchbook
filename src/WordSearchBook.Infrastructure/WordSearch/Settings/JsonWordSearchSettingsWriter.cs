using System.Text.Json;
using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Core.WordSearch.Domain;
using WordSearchBook.Core.WordSearch.Settings;

namespace WordSearchBook.Infrastructure.WordSearch.Settings;

public sealed class JsonWordSearchSettingsWriter(IWordSearchSettingsReader settingsReader) : IWordSearchSettingsWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task SaveGlobalAsync(
        string rootPath,
        GlobalWordSearchSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(settings);
        JsonWordSearchSettingsReader.ValidateGlobal(settings);

        var brandsRoot = Path.Combine(rootPath, "brands");
        if (Directory.Exists(brandsRoot))
        {
            foreach (var directory in Directory.EnumerateDirectories(brandsRoot))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await settingsReader.ReadBrandAsync(rootPath, Path.GetFileName(directory), settings, cancellationToken);
            }
        }

        await WriteAtomicallyAsync(Path.Combine(rootPath, "settings.json"), settings, cancellationToken);
    }

    public async Task SaveBrandAsync(
        string rootPath,
        string brandId,
        BrandWordSearchSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ValidateSegment(brandId, nameof(brandId));
        ArgumentNullException.ThrowIfNull(settings);
        var path = Path.Combine(rootPath, "brands", brandId, "settings.json");
        if (!File.Exists(path))
        {
            throw new WordSearchGenerationException("settings_not_found", "Only existing brands can be edited in this phase.");
        }

        var global = await settingsReader.ReadGlobalAsync(rootPath, cancellationToken);
        JsonWordSearchSettingsReader.ValidateBrand(global, settings);
        await WriteAtomicallyAsync(path, settings, cancellationToken);
    }

    private static async Task WriteAtomicallyAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new WordSearchGenerationException("settings_save_failed", "Settings could not be saved.", exception);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void ValidateSegment(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value is "." or ".." || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || value.Contains('/') || value.Contains('\\'))
        {
            throw new ArgumentException("Value must be a safe path segment.", parameterName);
        }
    }
}
