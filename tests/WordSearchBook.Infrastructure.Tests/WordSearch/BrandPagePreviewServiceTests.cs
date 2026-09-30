using System.Drawing;
using System.Drawing.Imaging;
using Microsoft.Extensions.DependencyInjection;
using WordSearchBook.Core.WordSearch.Application;
using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Infrastructure.DependencyInjection;
using WordSearchBook.Infrastructure.WordSearch.Settings;

namespace WordSearchBook.Infrastructure.Tests.WordSearch;

public sealed class BrandPagePreviewServiceTests
{
    [Fact]
    public async Task DrawsAndAtomicallyReplacesPreviewWithoutChangingLayoutOrCertificate()
    {
        var root = await CreateBrandRootAsync();
        try
        {
            var brandDirectory = Path.Combine(root, "brands", "demo");
            var layoutPath = Path.Combine(brandDirectory, "page_layout.png");
            var previewPath = Path.Combine(brandDirectory, BrandPagePreviewSample.OutputFileName);
            var layoutBefore = await File.ReadAllBytesAsync(layoutPath);
            await File.WriteAllTextAsync(previewPath, "old preview");
            using var services = BuildServices();

            var result = await services.GetRequiredService<IBrandPagePreviewService>()
                .DrawAsync(root, "demo");

            Assert.Equal("demo", result.BrandId);
            Assert.Equal("page_layout.preview.png", result.FileName);
            Assert.Equal(2588, result.Width);
            Assert.Equal(3375, result.Height);
            using (var preview = new Bitmap(previewPath))
            {
                Assert.Equal(2588, preview.Width);
                Assert.Equal(3375, preview.Height);
                Assert.Equal(ImageFormat.Png.Guid, preview.RawFormat.Guid);
            }

            Assert.Equal(layoutBefore, await File.ReadAllBytesAsync(layoutPath));
            Assert.False(File.Exists(Path.Combine(brandDirectory, "brand.validation.json")));
            Assert.Empty(Directory.EnumerateFiles(brandDirectory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PropagatesStableLayoutFailureWithoutPublishingPreview()
    {
        var root = await CreateBrandRootAsync();
        try
        {
            var brandDirectory = Path.Combine(root, "brands", "demo");
            var layoutPath = Path.Combine(brandDirectory, "page_layout.png");
            using (var invalid = new Bitmap(100, 100))
            {
                invalid.Save(layoutPath, ImageFormat.Png);
            }

            using var services = BuildServices();
            var exception = await Assert.ThrowsAsync<WordSearchGenerationException>(() =>
                services.GetRequiredService<IBrandPagePreviewService>().DrawAsync(root, "demo"));

            Assert.Equal("page_layout_invalid", exception.Code);
            Assert.False(File.Exists(Path.Combine(brandDirectory, BrandPagePreviewSample.OutputFileName)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddWordSearchBookInfrastructure();
        return services.BuildServiceProvider();
    }

    private static async Task<string> CreateBrandRootAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"word-search-preview-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var reader = new JsonWordSearchSettingsReader();
        await reader.ReadGlobalAsync(root);
        await new JsonWordSearchSettingsWriter(reader).CreateBrandAsync(root, "demo");
        return root;
    }
}
