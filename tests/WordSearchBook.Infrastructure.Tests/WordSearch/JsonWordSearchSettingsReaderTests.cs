using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Infrastructure.WordSearch.Settings;

namespace WordSearchBook.Infrastructure.Tests.WordSearch;

public sealed class JsonWordSearchSettingsReaderTests
{
    [Fact]
    public async Task LoadsValidGlobalAndBrandFixture()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "SingleTopicBook");

        var settings = await new JsonWordSearchSettingsReader().ReadAsync(root, "demo");

        Assert.Equal(20, settings.Global.Board.Width);
        Assert.Equal(3000, settings.Global.Page.Height);
        Assert.Equal(2000, settings.Brand.BoardGame.Rectangle.Width);
        Assert.Equal("Arial", settings.Brand.BoardGame.Font.Name);
        Assert.Equal("#8B1E1E", settings.Brand.AnswerLine.Color);
    }

    [Fact]
    public async Task RejectsBoardRectangleThatIsNotSquare()
    {
        var root = await CreateTemporarySettingsAsync(brandTransform: json =>
            json.Replace("\"width\": 2000, \"height\": 2000", "\"width\": 1800, \"height\": 2000", StringComparison.Ordinal));

        try
        {
            var exception = await Assert.ThrowsAsync<WordSearchGenerationException>(() =>
                new JsonWordSearchSettingsReader().ReadAsync(root, "demo"));

            Assert.Equal("settings_invalid", exception.Code);
            Assert.Contains("square", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsRectangleOutsidePage()
    {
        var root = await CreateTemporarySettingsAsync(brandTransform: json =>
            json.Replace("\"x\": 2100, \"y\": 2900", "\"x\": 2300, \"y\": 2950", StringComparison.Ordinal));

        try
        {
            var exception = await Assert.ThrowsAsync<WordSearchGenerationException>(() =>
                new JsonWordSearchSettingsReader().ReadAsync(root, "demo"));

            Assert.Equal("settings_invalid", exception.Code);
            Assert.Contains("inside", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsInvalidColor()
    {
        var root = await CreateTemporarySettingsAsync(brandTransform: json =>
            json.Replace("#8B1E1E", "dark-red", StringComparison.Ordinal));

        try
        {
            var exception = await Assert.ThrowsAsync<WordSearchGenerationException>(() =>
                new JsonWordSearchSettingsReader().ReadAsync(root, "demo"));

            Assert.Equal("settings_invalid", exception.Code);
            Assert.Contains("answerLine.color", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsMissingSettingsFile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"word-search-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var exception = await Assert.ThrowsAsync<WordSearchGenerationException>(() =>
                new JsonWordSearchSettingsReader().ReadAsync(root, "demo"));

            Assert.Equal("settings_not_found", exception.Code);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<string> CreateTemporarySettingsAsync(Func<string, string>? brandTransform = null)
    {
        var source = Path.Combine(AppContext.BaseDirectory, "TestData", "SingleTopicBook");
        var root = Path.Combine(Path.GetTempPath(), $"word-search-settings-{Guid.NewGuid():N}");
        var brandDirectory = Path.Combine(root, "brands", "demo");
        Directory.CreateDirectory(brandDirectory);

        var globalJson = await File.ReadAllTextAsync(Path.Combine(source, "settings.json"));
        var brandJson = await File.ReadAllTextAsync(Path.Combine(source, "brands", "demo", "settings.json"));
        await File.WriteAllTextAsync(Path.Combine(root, "settings.json"), globalJson);
        await File.WriteAllTextAsync(
            Path.Combine(brandDirectory, "settings.json"),
            brandTransform?.Invoke(brandJson) ?? brandJson);
        return root;
    }
}
