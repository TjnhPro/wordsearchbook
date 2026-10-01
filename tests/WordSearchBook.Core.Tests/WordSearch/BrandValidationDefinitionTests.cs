using WordSearchBook.Core.WordSearch.Validation;

namespace WordSearchBook.Core.Tests.WordSearch;

public sealed class BrandValidationDefinitionTests
{
    [Fact]
    public void SignatureIsDeterministicRegardlessOfEntryOrdering()
    {
        var forward = BrandValidationDefinition.CalculateSignature(BrandValidationDefinition.Entries);
        var reverse = BrandValidationDefinition.CalculateSignature(BrandValidationDefinition.Entries.Reverse());

        Assert.Equal(forward, reverse);
        Assert.StartsWith("sha256:", forward, StringComparison.Ordinal);
    }

    [Fact]
    public void DefinitionTracksRequiredLayoutsAndOptionalImageFolders()
    {
        Assert.Collection(
            BrandValidationDefinition.Entries,
            pageLayout => AssertRequiredLayout(pageLayout, BrandValidationDefinition.PageLayoutKey),
            frontLayout => AssertRequiredLayout(frontLayout, BrandValidationDefinition.FrontLayoutKey),
            qrPage => AssertOptionalPng(qrPage, BrandValidationDefinition.QrPageKey),
            front => AssertOptionalImageFolder(front, BrandValidationDefinition.FrontKey),
            back => AssertOptionalImageFolder(back, BrandValidationDefinition.BackKey));
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

    private static void AssertOptionalImageFolder(BrandValidationEntryDefinition entry, string expectedKey)
    {
        Assert.Equal(expectedKey, entry.Key);
        Assert.Equal(BrandValidationTargetKind.Directory, entry.TargetKind);
        Assert.False(entry.Required);
        Assert.False(entry.Recursive);
        Assert.Equal([".jpeg", ".jpg", ".png"], entry.Extensions);
    }

    private static void AssertRequiredLayout(BrandValidationEntryDefinition entry, string expectedKey)
    {
        Assert.Equal(expectedKey, entry.Key);
        Assert.Equal(BrandValidationTargetKind.File, entry.TargetKind);
        Assert.True(entry.Required);
        Assert.False(entry.Recursive);
        Assert.Equal([".png"], entry.Extensions);
    }

    private static void AssertOptionalPng(BrandValidationEntryDefinition entry, string expectedKey)
    {
        Assert.Equal(expectedKey, entry.Key);
        Assert.Equal(BrandValidationTargetKind.File, entry.TargetKind);
        Assert.False(entry.Required);
        Assert.False(entry.Recursive);
        Assert.Equal([".png"], entry.Extensions);
    }
}

