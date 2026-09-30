using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Core.WordSearch.Domain;
using WordSearchBook.Infrastructure.WordSearch.Settings;

namespace WordSearchBook.Infrastructure.Tests.WordSearch;

public sealed class JsonWordSearchSettingsWriterTests
{
    [Fact]
    public async Task SavesExistingBrandAtomicallyAndCanReadItBack()
    {
        var root = CopyFixtureToTemporaryRoot();
        try
        {
            var reader = new JsonWordSearchSettingsReader();
            var writer = new JsonWordSearchSettingsWriter(reader);
            var current = await reader.ReadAsync(root, "demo");
            var changed = current.Brand with { AnswerLine = new AnswerLineSettings(12, "#123456") };

            await writer.SaveBrandAsync(root, "demo", changed);
            var saved = await reader.ReadAsync(root, "demo");

            Assert.Equal(12, saved.Brand.AnswerLine.Width);
            Assert.Equal("#123456", saved.Brand.AnswerLine.Color);
            Assert.Empty(Directory.EnumerateFiles(Path.Combine(root, "brands", "demo"), "*.tmp"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task InvalidGlobalSaveKeepsPreviousFile()
    {
        var root = CopyFixtureToTemporaryRoot();
        try
        {
            var path = Path.Combine(root, "settings.json");
            var before = await File.ReadAllBytesAsync(path);
            var writer = new JsonWordSearchSettingsWriter(new JsonWordSearchSettingsReader());
            var incompatible = new GlobalWordSearchSettings(new BoardSize(20, 20), new PageSize(100, 100));

            var exception = await Assert.ThrowsAsync<WordSearchGenerationException>(() =>
                writer.SaveGlobalAsync(root, incompatible));

            Assert.Equal("settings_invalid", exception.Code);
            Assert.Equal(before, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CopyFixtureToTemporaryRoot()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "TestData", "SingleTopicBook");
        var destination = Path.Combine(Path.GetTempPath(), $"word-search-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = file.Replace(source, destination, StringComparison.Ordinal);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        return destination;
    }
}
