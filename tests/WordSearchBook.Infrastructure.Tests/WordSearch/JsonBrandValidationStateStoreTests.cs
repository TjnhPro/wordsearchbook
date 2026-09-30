using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Core.WordSearch.Validation;
using WordSearchBook.Infrastructure.WordSearch.Validation;

namespace WordSearchBook.Infrastructure.Tests.WordSearch;

public sealed class JsonBrandValidationStateStoreTests
{
    [Fact]
    public async Task SavesAndLoadsCertificateAtomically()
    {
        var root = CreateRoot();
        try
        {
            var store = new JsonBrandValidationStateStore();
            var record = CreateRecord();

            await store.SaveAsync(root, "demo", record);
            var loaded = await store.LoadAsync(root, "demo");

            Assert.NotNull(loaded);
            Assert.Equal(record.SchemaVersion, loaded.SchemaVersion);
            Assert.Equal(record.AssetFingerprintFormatVersion, loaded.AssetFingerprintFormatVersion);
            Assert.Equal(record.DefinitionChangedAtUtc, loaded.DefinitionChangedAtUtc);
            Assert.Equal(record.DefinitionSignature, loaded.DefinitionSignature);
            Assert.Equal(record.Fingerprint, loaded.Fingerprint);
            Assert.Equal(record.ValidatedAtUtc, loaded.ValidatedAtUtc);
            Assert.Equal(record.RequiresValidation, loaded.RequiresValidation);
            Assert.Equal(record.Assets, loaded.Assets);
            Assert.Empty(Directory.EnumerateFiles(Path.Combine(root, "brands", "demo"), "*.tmp"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MalformedCertificateFailsClosed()
    {
        var root = CreateRoot();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "brands", "demo", "brand.validation.json"), "{");
            var exception = await Assert.ThrowsAsync<WordSearchGenerationException>(async () =>
                await new JsonBrandValidationStateStore().LoadAsync(root, "demo"));

            Assert.Equal("brand_validation_state_unavailable", exception.Code);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    public async Task RejectsUnsafeBrandPath(string brandId)
    {
        var root = CreateRoot();
        try
        {
            var exception = await Assert.ThrowsAsync<WordSearchGenerationException>(async () =>
                await new JsonBrandValidationStateStore().LoadAsync(root, brandId));

            Assert.Equal("brand_name_invalid", exception.Code);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static BrandValidationRecord CreateRecord() => new(
        BrandValidationDefinition.SchemaVersion,
        BrandValidationDefinition.AssetFingerprintFormatVersion,
        BrandValidationDefinition.ChangedAtUtc,
        BrandValidationDefinition.Signature,
        "sha256:test",
        DateTimeOffset.UtcNow,
        false,
        [new BrandValidationAssetFact("page_layout.png", 2588, 3375)]);

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"wordsearchbook-validation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "brands", "demo"));
        return root;
    }
}
