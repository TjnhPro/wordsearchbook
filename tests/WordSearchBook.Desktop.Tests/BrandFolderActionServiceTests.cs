using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using WordSearchBook.Desktop.Bridge;

namespace WordSearchBook.Desktop.Tests;

public sealed class BrandFolderActionServiceTests
{
    [Fact]
    public async Task OpensResolvedBrandDirectoryWithShellExecution()
    {
        var root = CreateRootWithBrand();
        try
        {
            ProcessStartInfo? captured = null;
            var service = new BrandFolderActionService(startInfo =>
            {
                captured = startInfo;
                return null;
            });

            await service.OpenAsync(root, "demo");

            Assert.NotNull(captured);
            Assert.Equal(Path.Combine(root, "brands", "demo"), captured.FileName);
            Assert.True(captured.UseShellExecute);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("../demo")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    public async Task RejectsUnsafeBrandIdentifier(string brandId)
    {
        var root = CreateRootWithBrand();
        try
        {
            var service = new BrandFolderActionService(_ => null);
            await Assert.ThrowsAsync<ArgumentException>(async () => await service.OpenAsync(root, brandId));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReturnsStableErrorWhenBrandDirectoryDoesNotExist()
    {
        var root = CreateRootWithBrand();
        try
        {
            var service = new BrandFolderActionService(_ => null);
            var exception = await Assert.ThrowsAsync<BrandFolderActionException>(async () =>
                await service.OpenAsync(root, "missing"));

            Assert.Equal("brand_not_found", exception.Code);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReturnsStableErrorWhenShellCannotOpenDirectory()
    {
        var root = CreateRootWithBrand();
        try
        {
            var service = new BrandFolderActionService(_ => throw new Win32Exception("No shell"));
            var exception = await Assert.ThrowsAsync<BrandFolderActionException>(async () =>
                await service.OpenAsync(root, "demo"));

            Assert.Equal("brand_folder_open_failed", exception.Code);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateRootWithBrand()
    {
        var root = Path.Combine(Path.GetTempPath(), $"word-search-folder-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "brands", "demo"));
        return root;
    }
}
