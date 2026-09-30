using WordSearchBook.Core.WordSearch.Validation;
using WordSearchBook.Infrastructure.WordSearch.Validation;

namespace WordSearchBook.Infrastructure.Tests.WordSearch;

public sealed class CsvBookDataValidationServiceTests
{
    [Fact]
    public async Task ValidatesCsvAndReusesCertificateWithoutParsingOnRefresh()
    {
        var root = CopyFixtureToTemporaryRoot();
        try
        {
            var service = CreateService();

            var result = await service.ValidateAsync(root, "sample-book");
            var state = await service.CheckStateAsync(root, "sample-book");

            Assert.True(result.IsSuccess);
            Assert.Equal(BookDataValidationStatus.Validated, state.Status);
            Assert.StartsWith("sha256:", state.ContentHash, StringComparison.Ordinal);
            Assert.Equal(64, state.ContentHash!["sha256:".Length..].Length);
            var topic = Assert.Single(state.Topics!);
            Assert.Equal(20, topic.KeywordCount);
            Assert.True(topic.IsValid);
            Assert.True(File.Exists(Path.Combine(root, "input", "sample-book", ".workspace", "data.validation.json")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MetadataChangeMarksCertificateAsNeedingValidation()
    {
        var root = CopyFixtureToTemporaryRoot();
        try
        {
            var service = CreateService();
            await service.ValidateAsync(root, "sample-book");
            await File.AppendAllTextAsync(Path.Combine(root, "input", "sample-book", "data.csv"), Environment.NewLine);

            var state = await service.CheckStateAsync(root, "sample-book");

            Assert.Equal(BookDataValidationStatus.NeedsValidation, state.Status);
            Assert.Equal("book_data_fingerprint_changed", state.ReasonCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task KeywordLengthSettingChangeRequiresValidationAgain()
    {
        var root = CopyFixtureToTemporaryRoot();
        try
        {
            var service = CreateService();
            var result = await service.ValidateAsync(root, "sample-book", maximumKeywordLength: 13);

            var state = await service.CheckStateAsync(root, "sample-book", maximumKeywordLength: 12);

            Assert.True(result.IsSuccess);
            Assert.Equal(BookDataValidationStatus.NeedsValidation, state.Status);
            Assert.Equal("book_data_validation_rule_changed", state.ReasonCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReportsMultipleRowAndTopicFailuresAndPersistsInvalidState()
    {
        var root = CopyFixtureToTemporaryRoot();
        try
        {
            await File.WriteAllLinesAsync(
                Path.Combine(root, "input", "sample-book", "data.csv"),
                [
                    "Topic,Keyword,Word Search Key",
                    "Animals,This keyword is much too long,RED PANDA",
                    "Animals,,RED PANDA",
                    "Animals,Otter,RED PANDA"
                ]);
            var service = CreateService();

            var result = await service.ValidateAsync(root, "sample-book");
            var state = await service.CheckStateAsync(root, "sample-book");

            Assert.False(result.IsSuccess);
            Assert.Equal(BookDataValidationStatus.Invalid, state.Status);
            Assert.Contains(result.Failures, failure => failure.Code == "keyword_too_long" && failure.SourceRow == 2);
            Assert.Contains(result.Failures, failure => failure.Code == "keyword_invalid" && failure.SourceRow == 3);
            Assert.Contains(result.Failures, failure => failure.Code == "duplicate_word");
            Assert.Contains(result.Failures, failure => failure.Code == "topic_word_count_invalid");
            Assert.False(Assert.Single(state.Topics!).IsValid);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MissingCsvIsAStableInvalidCertificate()
    {
        var root = CopyFixtureToTemporaryRoot();
        try
        {
            File.Delete(Path.Combine(root, "input", "sample-book", "data.csv"));
            var service = CreateService();

            var result = await service.ValidateAsync(root, "sample-book");
            var state = await service.CheckStateAsync(root, "sample-book");

            Assert.Equal("input_not_found", Assert.Single(result.Failures).Code);
            Assert.Equal(BookDataValidationStatus.Invalid, state.Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static CsvBookDataValidationService CreateService() =>
        new(new JsonBookDataValidationStateStore());

    private static string CopyFixtureToTemporaryRoot()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "TestData", "SingleTopicBook");
        var destination = Path.Combine(Path.GetTempPath(), $"word-search-data-validation-{Guid.NewGuid():N}");
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
