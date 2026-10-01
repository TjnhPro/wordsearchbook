using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using WordSearchBook.Desktop.Bridge;

namespace WordSearchBook.Desktop.Tests;

public sealed class BookOutputFolderActionServiceTests
{
    [Fact]
    public async Task OpensResolvedBookOutputDirectoryWithShellExecution()
    {
        var root = CreateRootWithOutput();
        try
        {
            ProcessStartInfo? captured = null;
            var service = new BookOutputFolderActionService(startInfo =>
            {
                captured = startInfo;
                return null;
            });

            await service.OpenAsync(root, "demo");

            Assert.NotNull(captured);
            Assert.Equal(Path.Combine(root, "input", "demo", "output"), captured.FileName);
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
    public async Task RejectsUnsafeBookIdentifier(string bookId)
    {
        var root = CreateRootWithOutput();
        try
        {
            var service = new BookOutputFolderActionService(_ => null);
            await Assert.ThrowsAsync<ArgumentException>(async () => await service.OpenAsync(root, bookId));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReturnsStableErrorWhenOutputDoesNotExist()
    {
        var root = CreateRootWithOutput();
        try
        {
            var service = new BookOutputFolderActionService(_ => null);
            var exception = await Assert.ThrowsAsync<BookOutputFolderActionException>(async () =>
                await service.OpenAsync(root, "missing"));

            Assert.Equal("book_output_not_found", exception.Code);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReturnsStableErrorWhenShellCannotOpenDirectory()
    {
        var root = CreateRootWithOutput();
        try
        {
            var service = new BookOutputFolderActionService(_ => throw new Win32Exception("No shell"));
            var exception = await Assert.ThrowsAsync<BookOutputFolderActionException>(async () =>
                await service.OpenAsync(root, "demo"));

            Assert.Equal("book_output_open_failed", exception.Code);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateRootWithOutput()
    {
        var root = Path.Combine(Path.GetTempPath(), $"word-search-output-folder-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "input", "demo", "output"));
        return root;
    }
}
