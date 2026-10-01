using System.Drawing;
using System.Text.Json;
using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Core.WordSearch.Domain;
using WordSearchBook.Infrastructure.WordSearch.Settings;

namespace WordSearchBook.Infrastructure.Tests.WordSearch;

public sealed class JsonWordSearchSettingsWriterTests
{
    [Fact]
    public async Task CreatesNewBrandWithDefaultSettings()
    {
        var root = CopyFixtureToTemporaryRoot();
        try
        {
            var reader = new JsonWordSearchSettingsReader();
            var writer = new JsonWordSearchSettingsWriter(reader);

            await writer.CreateBrandAsync(root, "new-brand");
            var created = await reader.ReadAsync(root, "new-brand");

            Assert.Equal(2000, created.Brand.BoardGame.Rectangle.Width);
            Assert.Equal("#8B1E1E", created.Brand.AnswerLine.Color);
            Assert.True(File.Exists(Path.Combine(root, "brands", "new-brand", "settings.json")));
            var layoutPath = Path.Combine(root, "brands", "new-brand", "page_layout.png");
            Assert.True(File.Exists(layoutPath));
            using (var layout = Image.FromFile(layoutPath))
            {
                Assert.Equal(2588, layout.Width);
                Assert.Equal(3375, layout.Height);
            }
            var frontLayoutPath = Path.Combine(root, "brands", "new-brand", "front_layout.png");
            Assert.True(File.Exists(frontLayoutPath));
            using (var frontLayout = new Bitmap(frontLayoutPath))
            {
                Assert.Equal(2588, frontLayout.Width);
                Assert.Equal(3375, frontLayout.Height);
                Assert.InRange(frontLayout.HorizontalResolution, 299, 301);
                Assert.InRange(frontLayout.VerticalResolution, 299, 301);
                Assert.Equal(0, frontLayout.GetPixel(0, 0).A);
                Assert.Equal(0, frontLayout.GetPixel(frontLayout.Width - 1, frontLayout.Height - 1).A);
            }
            Assert.True(Directory.Exists(Path.Combine(root, "brands", "new-brand", "front")));
            Assert.True(Directory.Exists(Path.Combine(root, "brands", "new-brand", "back")));
            Assert.Empty(Directory.EnumerateDirectories(Path.Combine(root, "brands"), "*.tmp"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsDuplicateBrandWithoutChangingExistingSettings()
    {
        var root = CopyFixtureToTemporaryRoot();
        try
        {
            var path = Path.Combine(root, "brands", "demo", "settings.json");
            var before = await File.ReadAllBytesAsync(path);
            var writer = new JsonWordSearchSettingsWriter(new JsonWordSearchSettingsReader());

            var exception = await Assert.ThrowsAsync<WordSearchGenerationException>(() =>
                writer.CreateBrandAsync(root, "demo"));

            Assert.Equal("brand_already_exists", exception.Code);
            Assert.Equal(before, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("CON")]
    [InlineData("trailing.")]
    [InlineData("bad:name")]
    public async Task RejectsInvalidBrandFolderName(string brandId)
    {
        var root = CopyFixtureToTemporaryRoot();
        try
        {
            var writer = new JsonWordSearchSettingsWriter(new JsonWordSearchSettingsReader());

            var exception = await Assert.ThrowsAsync<WordSearchGenerationException>(() =>
                writer.CreateBrandAsync(root, brandId));

            Assert.Equal("brand_name_invalid", exception.Code);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

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
            var json = await File.ReadAllTextAsync(Path.Combine(root, "brands", "demo", "settings.json"));
            Assert.Contains("\"alignment\": \"Center\"", json, StringComparison.Ordinal);
            Assert.Contains("\"columns\"", json, StringComparison.Ordinal);
            Assert.Contains("\"stepY\": 80", json, StringComparison.Ordinal);
            using var document = JsonDocument.Parse(json);
            Assert.False(document.RootElement.GetProperty("topic").TryGetProperty("rectangle", out _));
            Assert.True(document.RootElement.GetProperty("quote").TryGetProperty("rectangle", out _));
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

            Assert.Equal("page_size_unsupported", exception.Code);
            Assert.Equal(before, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SavesMaximumKeywordLengthInGlobalSettings()
    {
        var root = CopyFixtureToTemporaryRoot();
        try
        {
            var reader = new JsonWordSearchSettingsReader();
            var writer = new JsonWordSearchSettingsWriter(reader);
            var current = await reader.ReadGlobalAsync(root);

            await writer.SaveGlobalAsync(root, current with { MaximumKeywordLength = 9 });
            var saved = await reader.ReadGlobalAsync(root);

            Assert.Equal(9, saved.MaximumKeywordLength);
            var json = await File.ReadAllTextAsync(Path.Combine(root, "settings.json"));
            Assert.Contains("\"maximumKeywordLength\": 9", json, StringComparison.Ordinal);
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
