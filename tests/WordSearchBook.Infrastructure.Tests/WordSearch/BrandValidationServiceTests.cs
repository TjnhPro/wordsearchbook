using System.Drawing;
using System.Drawing.Imaging;
using WordSearchBook.Core.WordSearch.Validation;
using WordSearchBook.Infrastructure.WordSearch.Validation;
using WordSearchBook.Infrastructure.WordSearch.Settings;
using WordSearchBook.Core.WordSearch.Domain;

namespace WordSearchBook.Infrastructure.Tests.WordSearch;

public sealed class BrandValidationServiceTests
{
    [Fact]
    public async Task NoCertificateReturnsNotValidatedWithoutInspectingImage()
    {
        var root = CreateRoot();
        try
        {
            await File.WriteAllTextAsync(LayoutPath(root), "not an image");

            var state = await CreateService().CheckStateAsync(root, "demo");

            Assert.Equal(BrandValidationStatus.NotValidated, state.Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CurrentCertificateUsesMetadataWithoutDecodingImage()
    {
        var root = CreateRoot();
        try
        {
            await File.WriteAllTextAsync(LayoutPath(root), "not an image");
            await SaveCurrentRecordAsync(root);

            var state = await CreateService().CheckStateAsync(root, "demo");

            Assert.Equal(BrandValidationStatus.Validated, state.Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ChangedMetadataNeedsValidationWithoutDecodingImage()
    {
        var root = CreateRoot();
        try
        {
            await File.WriteAllTextAsync(LayoutPath(root), "first");
            await SaveCurrentRecordAsync(root);
            await File.AppendAllTextAsync(LayoutPath(root), " changed");

            var state = await CreateService().CheckStateAsync(root, "demo");

            Assert.Equal(BrandValidationStatus.NeedsValidation, state.Status);
            Assert.Equal("brand_fingerprint_changed", state.ReasonCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ChangedFrontLayoutMetadataNeedsValidationWithoutDecodingImage()
    {
        var root = CreateRoot();
        try
        {
            await File.WriteAllTextAsync(LayoutPath(root), "layout metadata");
            await SaveCurrentRecordAsync(root);
            await using (var stream = new FileStream(FrontLayoutPath(root), FileMode.Append, FileAccess.Write, FileShare.None))
            {
                await stream.WriteAsync(new byte[] { 0 });
            }

            var state = await CreateService().CheckStateAsync(root, "demo");

            Assert.Equal(BrandValidationStatus.NeedsValidation, state.Status);
            Assert.Equal("brand_fingerprint_changed", state.ReasonCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task OutdatedCertificateNeedsValidationBeforeMetadataCheck()
    {
        var root = CreateRoot();
        try
        {
            var record = CurrentRecord("ignored") with
            {
                SchemaVersion = 1,
                AssetFingerprintFormatVersion = 1
            };
            await new JsonBrandValidationStateStore().SaveAsync(root, "demo", record);

            var state = await CreateService().CheckStateAsync(root, "demo");

            Assert.Equal("brand_validation_record_outdated", state.ReasonCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ValidRequiredLayoutsCreateCertificate()
    {
        var root = CreateRoot();
        try
        {
            SaveImage(LayoutPath(root), BrandValidationDefinition.PageWidth, BrandValidationDefinition.PageHeight, ImageFormat.Png);

            var result = await CreateService().ValidateAsync(root, "demo");
            var record = await new JsonBrandValidationStateStore().LoadAsync(root, "demo");

            Assert.True(result.IsSuccess);
            Assert.NotNull(record);
            Assert.False(record.RequiresValidation);
            Assert.Collection(
                record.Assets,
                asset => AssertLayoutFact(asset, "page_layout.png"),
                asset => AssertLayoutFact(asset, "front_layout.png"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ValidatesOptionalFrontAndBackImagesWhenPresent()
    {
        var root = CreateRoot();
        try
        {
            SaveImage(LayoutPath(root), BrandValidationDefinition.PageWidth, BrandValidationDefinition.PageHeight, ImageFormat.Png);
            var front = Path.Combine(root, "brands", "demo", "front");
            var back = Path.Combine(root, "brands", "demo", "back");
            Directory.CreateDirectory(front);
            Directory.CreateDirectory(back);
            SaveImage(Path.Combine(front, "opening.PNG"), 2588, 3375, ImageFormat.Png);
            SaveImage(Path.Combine(back, "closing.JPG"), 2588, 3375, ImageFormat.Jpeg);

            var result = await CreateService().ValidateAsync(root, "demo");

            Assert.True(result.IsSuccess);
            Assert.Equal(
                ["page_layout.png", "front_layout.png", "front/opening.png", "back/closing.jpg"],
                result.State.ValidatedAssets!.Select(asset => asset.RelativePath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ValidatesOptionalQrPageWhenConfigured()
    {
        var root = await CreateConfiguredBrandRootAsync();
        try
        {
            SaveImage(QrPagePath(root), 2588, 3375, ImageFormat.Png);

            var result = await CreateService().ValidateAsync(root, "demo");

            Assert.True(result.IsSuccess);
            Assert.Contains(result.State.ValidatedAssets!, asset => asset.RelativePath == "page_qr.png");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task QrPageRequiresSettings()
    {
        var root = await CreateConfiguredBrandRootAsync(includeQrSettings: false);
        try
        {
            SaveImage(QrPagePath(root), 2588, 3375, ImageFormat.Png);

            var result = await CreateService().ValidateAsync(root, "demo");

            Assert.Contains(result.Failures, failure => failure.Code == "qr_settings_required");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("corrupt", "qr_page_invalid")]
    [InlineData("wrong-size", "qr_page_dimensions_invalid")]
    [InlineData("wrong-format", "qr_page_format_invalid")]
    public async Task InvalidQrPageReturnsStableFailure(string scenario, string expectedCode)
    {
        var root = await CreateConfiguredBrandRootAsync();
        try
        {
            if (scenario == "corrupt") await File.WriteAllTextAsync(QrPagePath(root), "broken");
            if (scenario == "wrong-size") SaveImage(QrPagePath(root), 100, 100, ImageFormat.Png);
            if (scenario == "wrong-format") SaveImage(QrPagePath(root), 2588, 3375, ImageFormat.Jpeg);

            var result = await CreateService().ValidateAsync(root, "demo");

            Assert.Contains(result.Failures, failure => failure.Code == expectedCode && failure.Target == "page_qr.png");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AddingQrPageInvalidatesExistingCertificateWithoutDecoding()
    {
        var root = CreateRoot();
        try
        {
            await File.WriteAllTextAsync(LayoutPath(root), "layout metadata");
            await SaveCurrentRecordAsync(root);
            await File.WriteAllTextAsync(QrPagePath(root), "not decoded during state check");

            var state = await CreateService().CheckStateAsync(root, "demo");

            Assert.Equal(BrandValidationStatus.NeedsValidation, state.Status);
            Assert.Equal("brand_fingerprint_changed", state.ReasonCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task InvalidOptionalImageReturnsFailureForItsRelativePath()
    {
        var root = CreateRoot();
        try
        {
            SaveImage(LayoutPath(root), 2588, 3375, ImageFormat.Png);
            var front = Path.Combine(root, "brands", "demo", "front");
            Directory.CreateDirectory(front);
            SaveImage(Path.Combine(front, "wrong.png"), 100, 100, ImageFormat.Png);

            var result = await CreateService().ValidateAsync(root, "demo");

            var failure = Assert.Single(result.Failures, item => item.Code == "brand_asset_dimensions_invalid");
            Assert.Equal("front/wrong.png", failure.Target);
            Assert.Equal(BrandValidationStatus.NotValidated, result.State.Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CorruptOptionalImageReturnsReadableFailureForItsRelativePath()
    {
        var root = CreateRoot();
        try
        {
            SaveImage(LayoutPath(root), 2588, 3375, ImageFormat.Png);
            var front = Path.Combine(root, "brands", "demo", "front");
            Directory.CreateDirectory(front);
            await File.WriteAllTextAsync(Path.Combine(front, "broken.jpg"), "not an image");

            var result = await CreateService().ValidateAsync(root, "demo");

            var failure = Assert.Single(result.Failures, item => item.Code == "brand_asset_invalid");
            Assert.Equal("front/broken.jpg", failure.Target);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("delete")]
    [InlineData("rename")]
    [InlineData("modify")]
    public async Task TrackedImageMetadataChangesInvalidateCertificateWithoutDecoding(string change)
    {
        var root = CreateRoot();
        try
        {
            await File.WriteAllTextAsync(LayoutPath(root), "layout metadata");
            var front = Path.Combine(root, "brands", "demo", "front");
            Directory.CreateDirectory(front);
            var imagePath = Path.Combine(front, "tracked.png");
            await File.WriteAllTextAsync(imagePath, "image metadata");
            var fingerprint = BrandValidationService.CaptureFingerprint(root, "demo");
            var record = CurrentRecord(fingerprint) with
            {
                Assets =
                [
                    new BrandValidationAssetFact("page_layout.png", 2588, 3375),
                    new BrandValidationAssetFact("front_layout.png", 2588, 3375),
                    new BrandValidationAssetFact("front/tracked.png", 2588, 3375)
                ]
            };
            await new JsonBrandValidationStateStore().SaveAsync(root, "demo", record);
            var service = CreateService();
            Assert.Equal(BrandValidationStatus.Validated, (await service.CheckStateAsync(root, "demo")).Status);

            if (change == "delete") File.Delete(imagePath);
            if (change == "rename") File.Move(imagePath, Path.Combine(front, "renamed.png"));
            if (change == "modify") await File.AppendAllTextAsync(imagePath, " changed");

            var state = await service.CheckStateAsync(root, "demo");
            Assert.Equal(BrandValidationStatus.NeedsValidation, state.Status);
            Assert.Equal("brand_fingerprint_changed", state.ReasonCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task IgnoresUnsupportedAndNestedFilesButTracksTopLevelImages()
    {
        var root = CreateRoot();
        try
        {
            SaveImage(LayoutPath(root), 2588, 3375, ImageFormat.Png);
            var front = Path.Combine(root, "brands", "demo", "front");
            var nested = Path.Combine(front, "nested");
            Directory.CreateDirectory(nested);
            await File.WriteAllTextAsync(Path.Combine(front, "notes.txt"), "ignored");
            SaveImage(Path.Combine(nested, "nested.png"), 100, 100, ImageFormat.Png);
            var service = CreateService();

            Assert.True((await service.ValidateAsync(root, "demo")).IsSuccess);
            await File.AppendAllTextAsync(Path.Combine(front, "notes.txt"), " changed");
            SaveImage(Path.Combine(nested, "another.jpg"), 100, 100, ImageFormat.Jpeg);
            Assert.Equal(BrandValidationStatus.Validated, (await service.CheckStateAsync(root, "demo")).Status);

            SaveImage(Path.Combine(front, "tracked.png"), 2588, 3375, ImageFormat.Png);
            var changed = await service.CheckStateAsync(root, "demo");
            Assert.Equal(BrandValidationStatus.NeedsValidation, changed.Status);
            Assert.Equal("brand_fingerprint_changed", changed.ReasonCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("missing", "page_layout_not_found")]
    [InlineData("corrupt", "page_layout_invalid")]
    [InlineData("wrong-size", "page_layout_dimensions_invalid")]
    [InlineData("wrong-format", "page_layout_format_invalid")]
    public async Task InvalidLayoutReturnsStableFailureAndWritesNoFirstCertificate(string scenario, string expectedCode)
    {
        var root = CreateRoot();
        try
        {
            switch (scenario)
            {
                case "corrupt":
                    await File.WriteAllTextAsync(LayoutPath(root), "broken");
                    break;
                case "wrong-size":
                    SaveImage(LayoutPath(root), 100, 100, ImageFormat.Png);
                    break;
                case "wrong-format":
                    SaveImage(LayoutPath(root), 100, 100, ImageFormat.Jpeg);
                    break;
            }

            var result = await CreateService().ValidateAsync(root, "demo");

            Assert.Equal(BrandValidationStatus.NotValidated, result.State.Status);
            Assert.Contains(result.Failures, failure => failure.Code == expectedCode);
            Assert.False(File.Exists(Path.Combine(root, "brands", "demo", "brand.validation.json")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("missing", "front_layout_not_found")]
    [InlineData("corrupt", "front_layout_invalid")]
    [InlineData("wrong-size", "front_layout_dimensions_invalid")]
    [InlineData("wrong-format", "front_layout_format_invalid")]
    public async Task InvalidFrontLayoutReturnsStableFailureAndWritesNoFirstCertificate(string scenario, string expectedCode)
    {
        var root = CreateRoot();
        try
        {
            SaveImage(LayoutPath(root), 2588, 3375, ImageFormat.Png);
            var frontLayoutPath = FrontLayoutPath(root);
            switch (scenario)
            {
                case "missing":
                    File.Delete(frontLayoutPath);
                    break;
                case "corrupt":
                    await File.WriteAllTextAsync(frontLayoutPath, "broken");
                    break;
                case "wrong-size":
                    SaveImage(frontLayoutPath, 100, 100, ImageFormat.Png);
                    break;
                case "wrong-format":
                    SaveImage(frontLayoutPath, 2588, 3375, ImageFormat.Jpeg);
                    break;
            }

            var result = await CreateService().ValidateAsync(root, "demo");

            var failure = Assert.Single(result.Failures, item => item.Code == expectedCode);
            Assert.Equal("front_layout.png", failure.Target);
            Assert.False(File.Exists(Path.Combine(root, "brands", "demo", "brand.validation.json")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FailedRevalidationMarksPreviousCertificateRequired()
    {
        var root = CreateRoot();
        try
        {
            SaveImage(LayoutPath(root), BrandValidationDefinition.PageWidth, BrandValidationDefinition.PageHeight, ImageFormat.Png);
            var service = CreateService();
            Assert.True((await service.ValidateAsync(root, "demo")).IsSuccess);
            await File.WriteAllTextAsync(LayoutPath(root), "broken");

            var result = await service.ValidateAsync(root, "demo");
            var record = await new JsonBrandValidationStateStore().LoadAsync(root, "demo");

            Assert.Equal(BrandValidationStatus.NeedsValidation, result.State.Status);
            Assert.True(record!.RequiresValidation);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static BrandValidationService CreateService() =>
        new(new JsonBrandValidationStateStore(), new WordSearchBook.Infrastructure.WordSearch.Settings.JsonWordSearchSettingsReader());

    private static async Task SaveCurrentRecordAsync(string root)
    {
        var fingerprint = BrandValidationService.CaptureFingerprint(root, "demo");
        await new JsonBrandValidationStateStore().SaveAsync(root, "demo", CurrentRecord(fingerprint));
    }

    private static BrandValidationRecord CurrentRecord(string fingerprint) => new(
        BrandValidationDefinition.SchemaVersion,
        BrandValidationDefinition.AssetFingerprintFormatVersion,
        BrandValidationDefinition.ChangedAtUtc,
        BrandValidationDefinition.Signature,
        fingerprint,
        DateTimeOffset.UtcNow,
        false,
        [
            new BrandValidationAssetFact("page_layout.png", 2588, 3375),
            new BrandValidationAssetFact("front_layout.png", 2588, 3375)
        ]);

    private static void AssertLayoutFact(BrandValidationAssetFact asset, string relativePath)
    {
        Assert.Equal(relativePath, asset.RelativePath);
        Assert.Equal(2588, asset.Width);
        Assert.Equal(3375, asset.Height);
    }

    private static void SaveImage(string path, int width, int height, ImageFormat format)
    {
        using var bitmap = new Bitmap(width, height);
        bitmap.Save(path, format);
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"wordsearchbook-validation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "brands", "demo"));
        SaveImage(FrontLayoutPath(root), 2588, 3375, ImageFormat.Png);
        return root;
    }

    private static string LayoutPath(string root) => Path.Combine(root, "brands", "demo", "page_layout.png");

    private static string FrontLayoutPath(string root) => Path.Combine(root, "brands", "demo", "front_layout.png");

    private static string QrPagePath(string root) => Path.Combine(root, "brands", "demo", "page_qr.png");

    private static async Task<string> CreateConfiguredBrandRootAsync(bool includeQrSettings = true)
    {
        var root = Path.Combine(Path.GetTempPath(), $"wordsearchbook-qr-validation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var reader = new JsonWordSearchSettingsReader();
        await reader.ReadGlobalAsync(root);
        var writer = new JsonWordSearchSettingsWriter(reader);
        await writer.CreateBrandAsync(root, "demo");
        if (includeQrSettings)
        {
            await writer.SaveBrandAsync(
                root,
                "demo",
                WordSearchSettingsDefaults.CreateBrand() with
                {
                    QrPage = new QrPageSettings(994, 2400, 600, "example.com")
                });
        }

        return root;
    }
}
