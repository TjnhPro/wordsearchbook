using System.Text.Json;
using WordSearchBook.Core.Application.Workspace;
using WordSearchBook.Core.WordSearch.Contracts;

namespace WordSearchBook.Infrastructure.Workspace;

public sealed class JsonBookBrandAssignmentStore : IBookBrandAssignmentStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string statePath;
    private readonly SemaphoreSlim gate = new(1, 1);

    public JsonBookBrandAssignmentStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WordSearchBook",
            "workspace-state.json"))
    {
    }

    internal JsonBookBrandAssignmentStore(string statePath)
    {
        this.statePath = Path.GetFullPath(statePath);
    }

    public async Task<IReadOnlyDictionary<string, string>> ReadAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await ReadCoreAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SaveAsync(string bookId, string brandId, CancellationToken cancellationToken = default)
    {
        ValidateSegment(bookId, nameof(bookId));
        ValidateSegment(brandId, nameof(brandId));
        await gate.WaitAsync(cancellationToken);
        string? temporaryPath = null;
        try
        {
            var assignments = new Dictionary<string, string>(
                await ReadCoreAsync(cancellationToken),
                StringComparer.OrdinalIgnoreCase)
            {
                [bookId] = brandId
            };
            var directory = Path.GetDirectoryName(statePath)!;
            Directory.CreateDirectory(directory);
            temporaryPath = Path.Combine(directory, $".{Path.GetFileName(statePath)}.{Guid.NewGuid():N}.tmp");
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, assignments, JsonOptions, cancellationToken);
            }

            File.Move(temporaryPath, statePath, overwrite: true);
            temporaryPath = null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new WordSearchGenerationException("workspace_state_save_failed", "The local workspace state could not be saved.", exception);
        }
        finally
        {
            if (temporaryPath is not null)
            {
                File.Delete(temporaryPath);
            }

            gate.Release();
        }
    }

    private async Task<IReadOnlyDictionary<string, string>> ReadCoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(statePath))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            await using var stream = File.OpenRead(statePath);
            var values = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(stream, JsonOptions, cancellationToken);
            return new Dictionary<string, string>(values ?? [], StringComparer.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new WordSearchGenerationException("workspace_state_read_failed", "The local workspace state could not be read.", exception);
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
