using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Core.WordSearch.Domain;
using WordSearchBook.Core.WordSearch.Settings;

namespace WordSearchBook.Infrastructure.WordSearch.Settings;

public sealed class JsonWordSearchSettingsWriter(IWordSearchSettingsReader settingsReader) : IWordSearchSettingsWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private static readonly HashSet<string> ReservedFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public async Task CreateBrandAsync(
        string rootPath,
        string brandId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ValidateNewBrandId(brandId);

        var global = await settingsReader.ReadGlobalAsync(rootPath, cancellationToken);
        var settings = WordSearchSettingsDefaults.CreateBrand();
        JsonWordSearchSettingsReader.ValidateBrand(global, settings);

        var brandsRoot = Path.Combine(rootPath, "brands");
        var destination = Path.Combine(brandsRoot, brandId);
        var staging = Path.Combine(brandsRoot, $".{brandId}.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(brandsRoot);
            if (Directory.Exists(destination) || File.Exists(destination))
            {
                throw new WordSearchGenerationException("brand_already_exists", $"Brand '{brandId}' already exists.");
            }

            Directory.CreateDirectory(staging);
            await WriteAtomicallyAsync(Path.Combine(staging, "settings.json"), settings, cancellationToken);
            WriteDefaultPageLayout(Path.Combine(staging, "page_layout.png"));
            try
            {
                Directory.Move(staging, destination);
            }
            catch (IOException exception) when (Directory.Exists(destination) || File.Exists(destination))
            {
                throw new WordSearchGenerationException("brand_already_exists", $"Brand '{brandId}' already exists.", exception);
            }
        }
        catch (WordSearchGenerationException exception) when (exception.Code == "settings_save_failed")
        {
            throw new WordSearchGenerationException("brand_create_failed", "The brand could not be created.", exception);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or ExternalException)
        {
            throw new WordSearchGenerationException("brand_create_failed", "The brand could not be created.", exception);
        }
        finally
        {
            try
            {
                if (Directory.Exists(staging))
                {
                    Directory.Delete(staging, recursive: true);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The primary create result is more useful than a staging cleanup failure.
            }
        }
    }

    private static void WriteDefaultPageLayout(string path)
    {
        using var bitmap = new Bitmap(
            WordSearchSettingsDefaults.PageWidth,
            WordSearchSettingsDefaults.PageHeight,
            PixelFormat.Format32bppArgb);
        bitmap.SetResolution(300, 300);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
        }

        bitmap.Save(path, ImageFormat.Png);
    }

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

    private static void ValidateNewBrandId(string brandId)
    {
        if (string.IsNullOrWhiteSpace(brandId) ||
            brandId.Length > 64 ||
            !string.Equals(brandId, brandId.Trim(), StringComparison.Ordinal) ||
            brandId.EndsWith('.') ||
            brandId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            brandId.Contains('/') ||
            brandId.Contains('\\') ||
            brandId is "." or ".." ||
            ReservedFolderNames.Contains(brandId.Split('.')[0]))
        {
            throw new WordSearchGenerationException("brand_name_invalid", "Brand name must be a valid folder name with at most 64 characters.");
        }
    }
}
