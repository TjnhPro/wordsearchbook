using System.Reflection;
using WordSearchBook.Core.Application;

namespace WordSearchBook.Infrastructure;

public sealed class ApplicationInfoProvider : IApplicationInfoProvider
{
    public ApplicationInfo GetCurrent()
    {
        var version = typeof(ApplicationInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        return new ApplicationInfo(
            Name: "Word Search Book",
            Version: string.IsNullOrWhiteSpace(version) ? "unknown" : version,
            Status: "ready");
    }
}
