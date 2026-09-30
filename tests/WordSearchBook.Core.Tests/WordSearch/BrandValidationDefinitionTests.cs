using WordSearchBook.Core.WordSearch.Validation;

namespace WordSearchBook.Core.Tests.WordSearch;

public sealed class BrandValidationDefinitionTests
{
    [Fact]
    public void SignatureIsDeterministicRegardlessOfRuleOrdering()
    {
        var forward = BrandValidationDefinition.CalculateSignature(BrandValidationDefinition.Rules);
        var reverse = BrandValidationDefinition.CalculateSignature(BrandValidationDefinition.Rules.Reverse());

        Assert.Equal(forward, reverse);
        Assert.StartsWith("sha256:", forward, StringComparison.Ordinal);
    }

    [Fact]
    public void FingerprintChangesWhenTrackedMetadataChanges()
    {
        var timestamp = new DateTimeOffset(2026, 9, 30, 1, 2, 3, TimeSpan.Zero);
        var original = BrandAssetFingerprintCalculator.Calculate(
            [new BrandValidationFileMetadata("page_layout.png", 100, timestamp)]);
        var replaced = BrandAssetFingerprintCalculator.Calculate(
            [new BrandValidationFileMetadata("page_layout.png", 101, timestamp)]);
        var touched = BrandAssetFingerprintCalculator.Calculate(
            [new BrandValidationFileMetadata("page_layout.png", 100, timestamp.AddTicks(1))]);
        var missing = BrandAssetFingerprintCalculator.Calculate(
            [BrandValidationFileMetadata.Missing("page_layout.png")]);

        Assert.NotEqual(original, replaced);
        Assert.NotEqual(original, touched);
        Assert.NotEqual(original, missing);
    }

    [Theory]
    [InlineData("../page_layout.png")]
    [InlineData("assets/../page_layout.png")]
    [InlineData("C:\\page_layout.png")]
    public void RelativePathNormalizationRejectsUnsafePaths(string path)
    {
        Assert.Throws<ArgumentException>(() => BrandValidationDefinition.NormalizeRelativePath(path));
    }
}

