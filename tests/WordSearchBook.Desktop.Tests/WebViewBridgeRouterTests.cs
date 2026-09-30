using System.IO;
using System.Text.Json;
using WordSearchBook.Core.Application;
using WordSearchBook.Desktop.Bridge;

namespace WordSearchBook.Desktop.Tests;

public sealed class WebViewBridgeRouterTests
{
    private static readonly WebViewBridgeRouter Router = new(new StubApplicationInfoProvider());

    [Fact]
    public void ReturnsTypedPongForPing()
    {
        using var response = JsonDocument.Parse(Router.Handle("""{"id":"request-1","type":"ping"}"""));
        var root = response.RootElement;

        Assert.Equal("request-1", root.GetProperty("id").GetString());
        Assert.Equal("pong", root.GetProperty("type").GetString());
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal("Word Search Book", root.GetProperty("data").GetProperty("name").GetString());
        Assert.Equal("0.1.0", root.GetProperty("data").GetProperty("version").GetString());
        Assert.Equal("ready", root.GetProperty("data").GetProperty("status").GetString());
        Assert.False(root.TryGetProperty("error", out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-json")]
    [InlineData("{}")]
    public void ReturnsStableErrorForMalformedMessages(string? message)
    {
        using var response = JsonDocument.Parse(Router.Handle(message));
        var root = response.RootElement;

        Assert.Equal("error", root.GetProperty("type").GetString());
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("malformed_message", root.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void ReturnsStableErrorForUnsupportedMessages()
    {
        using var response = JsonDocument.Parse(Router.Handle("""{"id":"request-2","type":"unknown"}"""));
        var root = response.RootElement;

        Assert.Equal("request-2", root.GetProperty("id").GetString());
        Assert.Equal("unsupported_message", root.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void ResolvesFrontendEntryPointBelowBaseDirectory()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "word-search-book-tests");

        var result = FrontendPathResolver.GetIndexPath(baseDirectory);

        Assert.Equal(Path.GetFullPath(Path.Combine(baseDirectory, "Frontend", "index.html")), result);
    }

    private sealed class StubApplicationInfoProvider : IApplicationInfoProvider
    {
        public ApplicationInfo GetCurrent() => new("Word Search Book", "0.1.0", "ready");
    }
}
