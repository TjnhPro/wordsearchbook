using WordSearchBook.Core.Application;

namespace WordSearchBook.Core.Tests;

public sealed class ApplicationInfoTests
{
    [Fact]
    public void StoresApplicationMetadata()
    {
        var info = new ApplicationInfo("Word Search Book", "0.1.0", "ready");

        Assert.Equal("Word Search Book", info.Name);
        Assert.Equal("0.1.0", info.Version);
        Assert.Equal("ready", info.Status);
    }
}
