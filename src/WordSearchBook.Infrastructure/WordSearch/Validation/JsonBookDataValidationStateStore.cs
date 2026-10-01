using System.Text.Json;
using System.Text.Json.Serialization;
using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Core.WordSearch.Validation;

namespace WordSearchBook.Infrastructure.WordSearch.Validation;

public sealed class JsonBookDataValidationStateStore : IBookDataValidationStateStore
{
    internal const string FileName = "data.validation.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true
    };

    public async ValueTask<BookDataValidationRecord?> LoadAsync(
        string rootPath,
        string bookId,
        CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(rootPath, bookId);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<BookDataValidationRecord>(stream, JsonOptions, cancellationToken)
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
        string bookId,
        BookDataValidationRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        var path = ResolvePath(rootPath, bookId);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
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
                "book_data_validation_state_save_failed",
                $"Book data validation state could not be saved: {path}",
                exception);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // An unpublished temporary file is harmless and may be cleaned later.
            }
        }
    }

    internal static string ResolveBookDirectory(string rootPath, string bookId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ValidateBookId(bookId);
        var root = Path.GetFullPath(rootPath);
        var inputDirectory = Path.GetFullPath(Path.Combine(root, "input"));
        var bookDirectory = Path.GetFullPath(Path.Combine(inputDirectory, bookId));
        var expectedPrefix = inputDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!bookDirectory.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(bookDirectory))
        {
            throw new WordSearchGenerationException("book_not_found", $"Book directory was not found: {bookId}");
        }

        return bookDirectory;
    }

    internal static string ResolveDataPath(string rootPath, string bookId) =>
        Path.Combine(ResolveBookDirectory(rootPath, bookId), "data.csv");

    internal static string ResolvePath(string rootPath, string bookId) =>
        Path.Combine(ResolveBookDirectory(rootPath, bookId), ".workspace", FileName);

    internal static void ValidateBookId(string bookId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        if (bookId is "." or ".." ||
            bookId.Contains('/') ||
            bookId.Contains('\\') ||
            bookId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new WordSearchGenerationException("book_name_invalid", "Book name must be a single safe folder name.");
        }
    }

    private static WordSearchGenerationException Unavailable(string path, Exception? innerException = null) =>
        new("book_data_validation_state_unavailable", $"Book data validation state could not be read: {path}", innerException);
}
