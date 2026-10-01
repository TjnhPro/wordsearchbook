using System.IO;
using System.Text;

namespace WordSearchBook.Desktop.Tests;

public sealed class FrontendResourceProviderTests
{
    [Theory]
    [InlineData("https://app.wordsearchbook.invalid/index.html", "text/html; charset=utf-8")]
    [InlineData("https://app.wordsearchbook.invalid/css/tailwind.css", "text/css; charset=utf-8")]
    [InlineData("https://app.wordsearchbook.invalid/js/app.js", "text/javascript; charset=utf-8")]
    [InlineData("https://app.wordsearchbook.invalid/assets/wordsearchbook-icon-1024.png", "image/png")]
    public void OpensAllowlistedEmbeddedResources(string requestUri, string expectedContentType)
    {
        using var resource = new FrontendResourceProvider().Open(requestUri);

        Assert.NotNull(resource);
        Assert.Equal(expectedContentType, resource.ContentType);
        Assert.True(resource.Content.Length > 0);
    }

    [Theory]
    [InlineData("https://app.wordsearchbook.invalid/missing.js")]
    [InlineData("https://example.com/index.html")]
    [InlineData("http://app.wordsearchbook.invalid/index.html")]
    [InlineData("not-a-uri")]
    public void RejectsUnknownOrForeignResources(string requestUri)
    {
        Assert.Null(new FrontendResourceProvider().Open(requestUri));
    }

    [Fact]
    public void EmbeddedIndexReferencesTheEmbeddedLogoAndAssets()
    {
        using var resource = new FrontendResourceProvider().Open(FrontendResourceProvider.IndexUri.AbsoluteUri);
        Assert.NotNull(resource);
        using var reader = new StreamReader(resource.Content, Encoding.UTF8);
        var html = reader.ReadToEnd();

        Assert.Contains("./assets/wordsearchbook-icon-1024.png", html, StringComparison.Ordinal);
        Assert.Contains("./css/tailwind.css", html, StringComparison.Ordinal);
        Assert.Contains("./js/app.js", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">WS<", html, StringComparison.Ordinal);
    }
}
