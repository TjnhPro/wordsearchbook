using System.IO;

namespace WordSearchBook.Desktop;

public interface IApplicationRootProvider
{
    string RootPath { get; }
}

internal sealed class ExecutableApplicationRootProvider : IApplicationRootProvider
{
    public string RootPath { get; } = Path.GetFullPath(AppContext.BaseDirectory);
}
