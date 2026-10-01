using System.IO;
using System.Reflection;

namespace WordSearchBook.Desktop;

internal sealed class FrontendResourceProvider
{
    internal static readonly Uri ApplicationOrigin = new("https://app.wordsearchbook.invalid/");
    internal static readonly Uri IndexUri = new(ApplicationOrigin, "index.html");

    private static readonly IReadOnlyDictionary<string, ResourceDefinition> Resources =
        new Dictionary<string, ResourceDefinition>(StringComparer.Ordinal)
        {
            ["/index.html"] = new("WordSearchBook.Desktop.Frontend.index.html", "text/html; charset=utf-8"),
            ["/css/tailwind.css"] = new("WordSearchBook.Desktop.Frontend.css.tailwind.css", "text/css; charset=utf-8"),
            ["/js/app.js"] = new("WordSearchBook.Desktop.Frontend.js.app.js", "text/javascript; charset=utf-8"),
            ["/assets/wordsearchbook-icon-1024.png"] = new(
                "WordSearchBook.Desktop.Frontend.assets.wordsearchbook-icon-1024.png",
                "image/png")
        };

    private readonly Assembly assembly;

    internal FrontendResourceProvider()
        : this(typeof(FrontendResourceProvider).Assembly)
    {
    }

    internal FrontendResourceProvider(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        this.assembly = assembly;
    }

    internal FrontendResource? Open(string requestUri)
    {
        if (!Uri.TryCreate(requestUri, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, ApplicationOrigin.Host, StringComparison.OrdinalIgnoreCase) ||
            uri.Port != 443 ||
            !Resources.TryGetValue(uri.AbsolutePath, out var definition))
        {
            return null;
        }

        var content = assembly.GetManifestResourceStream(definition.ManifestName) ??
            throw new InvalidOperationException($"Embedded frontend resource '{definition.ManifestName}' was not found.");
        return new FrontendResource(content, definition.ContentType);
    }

    private sealed record ResourceDefinition(string ManifestName, string ContentType);
}

internal sealed record FrontendResource(Stream Content, string ContentType) : IDisposable
{
    public void Dispose() => Content.Dispose();
}
