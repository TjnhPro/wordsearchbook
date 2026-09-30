using System.Drawing;
using System.Drawing.Imaging;
using WordSearchBook.Core.WordSearch.Validation;
using WordSearchBook.Infrastructure.WordSearch.Validation;

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
    public async Task OutdatedCertificateNeedsValidationBeforeMetadataCheck()
    {
        var root = CreateRoot();
        try
        {
            var record = CurrentRecord("ignored") with { SchemaVersion = 0 };
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
    public async Task ValidPngCreatesCertificate()
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
            Assert.Collection(record.Assets, asset =>
            {
                Assert.Equal("page_layout.png", asset.RelativePath);
                Assert.Equal(2588, asset.Width);
                Assert.Equal(3375, asset.Height);
            });
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

    private static BrandValidationService CreateService() => new(new JsonBrandValidationStateStore());

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
        [new BrandValidationAssetFact("page_layout.png", 2588, 3375)]);

    private static void SaveImage(string path, int width, int height, ImageFormat format)
    {
        using var bitmap = new Bitmap(width, height);
        bitmap.Save(path, format);
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"wordsearchbook-validation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "brands", "demo"));
        return root;
    }

    private static string LayoutPath(string root) => Path.Combine(root, "brands", "demo", "page_layout.png");
}
