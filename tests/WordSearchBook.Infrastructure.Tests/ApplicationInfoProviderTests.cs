using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using WordSearchBook.Core.Application;
using WordSearchBook.Infrastructure.DependencyInjection;

namespace WordSearchBook.Infrastructure.Tests;

public sealed class ApplicationInfoProviderTests
{
    [Fact]
    public void ReturnsReadyApplicationInformation()
    {
        var info = new ApplicationInfoProvider().GetCurrent();
        var expectedVersion = typeof(ApplicationInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        Assert.Equal("Word Search Book", info.Name);
        Assert.False(string.IsNullOrWhiteSpace(expectedVersion));
        Assert.Equal(expectedVersion, info.Version);
        Assert.Equal("ready", info.Status);
    }

    [Fact]
    public void RegistersProviderWithDependencyInjection()
    {
        var services = new ServiceCollection();
        services.AddWordSearchBookInfrastructure();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<ApplicationInfoProvider>(provider.GetRequiredService<IApplicationInfoProvider>());
    }
}
