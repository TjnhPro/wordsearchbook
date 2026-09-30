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

        Assert.Equal("Word Search Book", info.Name);
        Assert.Equal("0.1.0", info.Version);
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
