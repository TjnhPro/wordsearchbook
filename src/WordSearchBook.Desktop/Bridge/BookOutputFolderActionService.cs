using System.Diagnostics;
using System.IO;

namespace WordSearchBook.Desktop.Bridge;

public interface IBookOutputFolderActionService
{
    ValueTask OpenAsync(string rootPath, string bookId, CancellationToken cancellationToken = default);
}

internal sealed class BookOutputFolderActionService : IBookOutputFolderActionService
{
    private readonly Func<ProcessStartInfo, Process?> startProcess;

    public BookOutputFolderActionService()
        : this(Process.Start)
    {
    }

    internal BookOutputFolderActionService(Func<ProcessStartInfo, Process?> startProcess)
    {
        this.startProcess = startProcess;
    }

    public ValueTask OpenAsync(
        string rootPath,
        string bookId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ValidateBookId(bookId);
        var inputDirectory = Path.GetFullPath(Path.Combine(rootPath, "input"));
        var bookDirectory = Path.GetFullPath(Path.Combine(inputDirectory, bookId));
        var expectedPrefix = inputDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!bookDirectory.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("bookId must resolve below the input directory.", nameof(bookId));
        }

        var outputDirectory = Path.Combine(bookDirectory, "output");
        if (!Directory.Exists(outputDirectory))
        {
            throw new BookOutputFolderActionException(
                "book_output_not_found",
                $"Book output directory was not found: {bookId}");
        }

        try
        {
            using var process = startProcess(new ProcessStartInfo(outputDirectory)
            {
                UseShellExecute = true
            });
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw new BookOutputFolderActionException(
                "book_output_open_failed",
                $"Book output directory could not be opened: {bookId}",
                exception);
        }

        return ValueTask.CompletedTask;
    }

    private static void ValidateBookId(string bookId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        if (bookId is "." or ".." ||
            bookId.Contains('/') ||
            bookId.Contains('\\') ||
            bookId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("bookId must be a single safe path segment.", nameof(bookId));
        }
    }
}

internal sealed class BookOutputFolderActionException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}
