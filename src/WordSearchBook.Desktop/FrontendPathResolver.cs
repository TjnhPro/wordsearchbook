using System.IO;

namespace WordSearchBook.Desktop;

internal static class FrontendPathResolver
{
    internal static string GetIndexPath(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        return Path.GetFullPath(Path.Combine(baseDirectory, "Frontend", "index.html"));
    }
}
