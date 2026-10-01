using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Core.WordSearch.Domain;
using WordSearchBook.Infrastructure.WordSearch.Settings;
using System.Text.Json.Nodes;

namespace WordSearchBook.Infrastructure.Tests.WordSearch;

public sealed class JsonWordSearchSettingsReaderTests
{
    [Fact]
    public async Task CreatesDefaultGlobalSettingsWhenFileIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"word-search-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var settings = await new JsonWordSearchSettingsReader().ReadGlobalAsync(root);

            Assert.Equal(20, settings.Board.Width);
            Assert.Equal(20, settings.Board.Height);
            Assert.Equal(2588, settings.Page.Width);
            Assert.Equal(3375, settings.Page.Height);
            Assert.Equal(13, settings.MaximumKeywordLength);
            Assert.Equal(4, settings.MaximumProcessingConcurrency);
            var json = await File.ReadAllTextAsync(Path.Combine(root, "settings.json"));
            Assert.Contains("\"board\"", json, StringComparison.Ordinal);
            Assert.Contains("\"page\"", json, StringComparison.Ordinal);
            Assert.Contains("\"maximumKeywordLength\": 13", json, StringComparison.Ordinal);
            Assert.Contains("\"maximumProcessingConcurrency\": 4", json, StringComparison.Ordinal);
            Assert.Empty(Directory.EnumerateFiles(root, "*.tmp"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LoadsValidGlobalAndBrandFixture()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "SingleTopicBook");

        var settings = await new JsonWordSearchSettingsReader().ReadAsync(root, "demo");

        Assert.Equal(20, settings.Global.Board.Width);
        Assert.Equal(3375, settings.Global.Page.Height);
        Assert.Equal(13, settings.Global.MaximumKeywordLength);
        Assert.Equal(new LayoutRectangle(300, 3000, 1988, 220), settings.Brand.Quote.Rectangle);
        Assert.Equal(2000, settings.Brand.BoardGame.Rectangle.Width);
        Assert.Equal(TextAlignment.Center, settings.Brand.Topic.Alignment);
        Assert.Equal(4, settings.Brand.KeywordList.Columns.Count);
        Assert.Equal(80, settings.Brand.KeywordList.StepY);
        Assert.Equal("Arial", settings.Brand.BoardGame.Font.Name);
        Assert.Equal("#8B1E1E", settings.Brand.AnswerLine.Color);
    }

    [Fact]
    public async Task LoadsDefaultQuoteForLegacyBrandButBlocksStrictReadUntilSaved()
    {
        var root = await CreateTemporarySettingsAsync(brandTransform: json =>
        {
            var document = JsonNode.Parse(json)!.AsObject();
            document.Remove("quote");
            return document.ToJsonString();
        });

        try
        {
            var reader = new JsonWordSearchSettingsReader();
            var global = await reader.ReadGlobalAsync(root);

            var legacy = await reader.ReadBrandAsync(root, "demo", global);

            Assert.True(legacy.RequiresSave);
            Assert.Equal(new LayoutRectangle(300, 3000, 1988, 220), legacy.Settings.Quote.Rectangle);
            var exception = await Assert.ThrowsAsync<WordSearchGenerationException>(() => reader.ReadAsync(root, "demo"));
            Assert.Equal("brand_settings_update_required", exception.Code);

            await new JsonWordSearchSettingsWriter(reader).SaveBrandAsync(root, "demo", legacy.Settings);
            var upgraded = await reader.ReadAsync(root, "demo");
            Assert.Equal(legacy.Settings.Quote, upgraded.Brand.Quote);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RejectsQuoteRectangleOutsidePage()
    {
        var settings = WordSearchSettingsDefaults.CreateBrand() with
        {
            Quote = WordSearchSettingsDefaults.CreateBrand().Quote with
            {
                Rectangle = new LayoutRectangle(2500, 3000, 1988, 220)
            }
        };

        var exception = Assert.Throws<WordSearchGenerationException>(() =>
            JsonWordSearchSettingsReader.ValidateBrand(WordSearchSettingsDefaults.CreateGlobal(), settings));

        Assert.Equal("settings_invalid", exception.Code);
        Assert.Contains("quote.rectangle", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadsAndNormalizesOptionalQrPageSettings()
    {
        var root = await CreateTemporarySettingsAsync(brandTransform: json =>
        {
            var document = JsonNode.Parse(json)!.AsObject();
            document["qrPage"] = JsonNode.Parse("""{"x":994,"y":2400,"size":600,"domainName":"  EXAMPLE.COM  "}""");
            return document.ToJsonString();
        });

        try
        {
            var settings = await new JsonWordSearchSettingsReader().ReadAsync(root, "demo");

            Assert.Equal(new QrPageSettings(994, 2400, 600, "example.com"), settings.Brand.QrPage);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExistingQrTemplateRequiresQrSettingsBeforeStrictRead()
    {
        var root = await CreateTemporarySettingsAsync();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "brands", "demo", "page_qr.png"), "metadata only");
            var reader = new JsonWordSearchSettingsReader();
            var global = await reader.ReadGlobalAsync(root);

            var brand = await reader.ReadBrandAsync(root, "demo", global);
            var exception = await Assert.ThrowsAsync<WordSearchGenerationException>(() => reader.ReadAsync(root, "demo"));

            Assert.True(brand.RequiresSave);
            Assert.Equal("qr_settings_required", brand.UpdateReasonCode);
            Assert.Null(brand.Settings.QrPage);
            Assert.Equal("qr_settings_required", exception.Code);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("example.com/path")]
    [InlineData("localhost")]
    [InlineData("-bad.example.com")]
    [InlineData("example.com:443")]
    public void RejectsInvalidQrDomain(string domainName)
    {
        var settings = WordSearchSettingsDefaults.CreateBrand() with
        {
            QrPage = new QrPageSettings(100, 100, 600, domainName)
        };

        var exception = Assert.Throws<WordSearchGenerationException>(() =>
            JsonWordSearchSettingsReader.ValidateBrand(WordSearchSettingsDefaults.CreateGlobal(), settings));

        Assert.Equal("qr_domain_invalid", exception.Code);
    }

    [Theory]
    [InlineData(-1, 0, 100)]
    [InlineData(0, -1, 100)]
    [InlineData(0, 0, 0)]
    [InlineData(2500, 0, 100)]
    [InlineData(0, 3300, 100)]
    public void RejectsQrSquareOutsidePage(int x, int y, int size)
    {
        var settings = WordSearchSettingsDefaults.CreateBrand() with
        {
            QrPage = new QrPageSettings(x, y, size, "example.com")
        };

        var exception = Assert.Throws<WordSearchGenerationException>(() =>
            JsonWordSearchSettingsReader.ValidateBrand(WordSearchSettingsDefaults.CreateGlobal(), settings));

        Assert.Equal("qr_settings_invalid", exception.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void RejectsProcessingConcurrencyOutsideSupportedRange(int value)
    {
        var exception = Assert.Throws<WordSearchGenerationException>(() =>
            JsonWordSearchSettingsReader.ValidateGlobal(
                WordSearchSettingsDefaults.CreateGlobal() with { MaximumProcessingConcurrency = value }));

        Assert.Equal("maximum_processing_concurrency_invalid", exception.Code);
        Assert.Contains("between 1 and 12", exception.Message, StringComparison.Ordinal);
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
    public async Task RejectsAnchorOutsidePage()
    {
        var root = await CreateTemporarySettingsAsync(brandTransform: json =>
            json.Replace("\"x\": 2100, \"y\": 2900", "\"x\": 2600, \"y\": 2900", StringComparison.Ordinal));

        try
        {
            var exception = await Assert.ThrowsAsync<WordSearchGenerationException>(() =>
                new JsonWordSearchSettingsReader().ReadAsync(root, "demo"));

            Assert.Equal("settings_invalid", exception.Code);
            Assert.Contains("anchor", exception.Message, StringComparison.OrdinalIgnoreCase);
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
    public async Task RejectsMissingBrandSettingsFile()
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
