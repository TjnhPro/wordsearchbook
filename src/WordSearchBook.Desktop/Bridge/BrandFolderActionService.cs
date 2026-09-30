using System.Diagnostics;
using System.IO;

namespace WordSearchBook.Desktop.Bridge;

public interface IBrandFolderActionService
{
    ValueTask OpenAsync(string rootPath, string brandId, CancellationToken cancellationToken = default);
}

internal sealed class BrandFolderActionService : IBrandFolderActionService
{
    private readonly Func<ProcessStartInfo, Process?> startProcess;

    public BrandFolderActionService()
        : this(Process.Start)
    {
    }

    internal BrandFolderActionService(Func<ProcessStartInfo, Process?> startProcess)
    {
        this.startProcess = startProcess;
    }

    public ValueTask OpenAsync(
        string rootPath,
        string brandId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ValidateBrandId(brandId);

        var brandsDirectory = Path.GetFullPath(Path.Combine(rootPath, "brands"));
        var brandDirectory = Path.GetFullPath(Path.Combine(brandsDirectory, brandId));
        var expectedPrefix = brandsDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!brandDirectory.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("brandId must resolve below the brands directory.", nameof(brandId));
        }

        if (!Directory.Exists(brandDirectory))
        {
            throw new BrandFolderActionException("brand_not_found", $"Brand directory was not found: {brandId}");
        }

        try
        {
            using var process = startProcess(new ProcessStartInfo(brandDirectory)
            {
                UseShellExecute = true
            });
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw new BrandFolderActionException(
                "brand_folder_open_failed",
                $"Brand directory could not be opened: {brandId}",
                exception);
        }

        return ValueTask.CompletedTask;
    }

    private static void ValidateBrandId(string brandId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(brandId);
        if (brandId is "." or ".." ||
            brandId.Contains('/') ||
            brandId.Contains('\\') ||
            brandId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("brandId must be a single safe path segment.", nameof(brandId));
        }
    }
}

internal sealed class BrandFolderActionException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}
