using System.Text;
using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Infrastructure.WordSearch.Input;

namespace WordSearchBook.Infrastructure.Tests.WordSearch;

public sealed class CsvWordSearchInputReaderTests
{
    [Fact]
    public async Task ReadsAndNormalizesSingleTopicFixture()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "TestData",
            "SingleTopicBook",
            "input",
            "sample-book",
            "data.csv");

        var topics = await new CsvWordSearchInputReader().ReadAsync(path);

        var topic = Assert.Single(topics);
        Assert.Equal(1, topic.Index);
        Assert.Equal("AMAZING ANIMALS", topic.Name);
        Assert.Equal(20, topic.Entries.Count);
        Assert.Equal("RED PANDA", topic.Entries[0].Keyword);
        Assert.Equal("REDPANDA", topic.Entries[0].WordSearchKey);
        Assert.Equal("CORALSNAKE", topic.Entries[^1].WordSearchKey);
    }

    [Fact]
    public async Task PreservesQuotedCommaInDisplayKeyword()
    {
        var rows = CreateValidRows();
        rows[0] = "Animals,\"Panda, Red\",RED PANDA";
        var path = await WriteTemporaryCsvAsync("Topic,Keyword,Word Search Key", rows);

        try
        {
            var topic = Assert.Single(await new CsvWordSearchInputReader().ReadAsync(path));

            Assert.Equal("PANDA, RED", topic.Entries[0].Keyword);
            Assert.Equal("REDPANDA", topic.Entries[0].WordSearchKey);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task GroupsTopicsAfterUppercaseNormalization()
    {
        var rows = CreateValidRows();
        rows[0] = "  animals  ,  Red Panda  ,RED PANDA";
        rows[1] = "ANIMALS,Blue Whale,BLUE WHALE";
        var path = await WriteTemporaryCsvAsync("Topic,Keyword,Word Search Key", rows);

        try
        {
            var topic = Assert.Single(await new CsvWordSearchInputReader().ReadAsync(path));

            Assert.Equal("ANIMALS", topic.Name);
            Assert.Equal("RED PANDA", topic.Entries[0].Keyword);
            Assert.Equal("BLUE WHALE", topic.Entries[1].Keyword);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task RejectsTopicWithFewerThanTwentyEntries()
    {
        var path = await WriteTemporaryCsvAsync(
            "Topic,Keyword,Word Search Key",
            ["Animals,Red Panda,RED PANDA"]);

        try
        {
            var exception = await Assert.ThrowsAsync<WordSearchGenerationException>(() =>
                new CsvWordSearchInputReader().ReadAsync(path));

            Assert.Equal("topic_word_count_invalid", exception.Code);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task RejectsMissingRequiredHeader()
    {
        var path = await WriteTemporaryCsvAsync("Topic,Keyword", ["Animals,Red Panda"]);

        try
        {
            var exception = await Assert.ThrowsAsync<WordSearchGenerationException>(() =>
                new CsvWordSearchInputReader().ReadAsync(path));

            Assert.Equal("csv_header_missing", exception.Code);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task RejectsDuplicateNormalizedWordSearchKey()
    {
        var rows = CreateValidRows();
        rows[1] = "Animals,Duplicate,WORDAB";
        var path = await WriteTemporaryCsvAsync("Topic,Keyword,Word Search Key", rows);

        try
        {
            var exception = await Assert.ThrowsAsync<WordSearchGenerationException>(() =>
                new CsvWordSearchInputReader().ReadAsync(path));

            Assert.Equal("duplicate_word", exception.Code);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task UsesConfiguredMaximumKeywordLengthIgnoringWhitespace()
    {
        var rows = CreateValidRows();
        rows[0] = "Animals,Too Long,WORDZZ";
        var path = await WriteTemporaryCsvAsync("Topic,Keyword,Word Search Key", rows);

        try
        {
            var exception = await Assert.ThrowsAsync<WordSearchGenerationException>(() =>
                new CsvWordSearchInputReader().ReadAsync(path, maximumKeywordLength: 6));

            Assert.Equal("keyword_too_long", exception.Code);
            Assert.Contains("CSV row 2, topic 'ANIMALS'", exception.Message, StringComparison.Ordinal);
            Assert.Contains("has 7 characters excluding spaces; the maximum is 6", exception.Message, StringComparison.Ordinal);
            Assert.Contains("change Max Keyword characters in Global Settings", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("", "word_invalid", "Word Search Key is empty. Enter letters A-Z.")]
    [InlineData("BAD-KEY", "word_invalid", "contains unsupported characters. Use letters A-Z only")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTU", "word_too_long", "has 21 letters; the maximum is 20")]
    public async Task ReportsActionableWordSearchKeyErrors(
        string wordSearchKey,
        string expectedCode,
        string expectedMessage)
    {
        var rows = CreateValidRows();
        rows[0] = $"Animals,Keyword 1,{wordSearchKey}";
        var path = await WriteTemporaryCsvAsync("Topic,Keyword,Word Search Key", rows);

        try
        {
            var exception = await Assert.ThrowsAsync<WordSearchGenerationException>(() =>
                new CsvWordSearchInputReader().ReadAsync(path));

            Assert.Equal(expectedCode, exception.Code);
            Assert.Contains("CSV row 2, topic 'ANIMALS'", exception.Message, StringComparison.Ordinal);
            Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string[] CreateValidRows() => Enumerable.Range(1, 20)
        .Select(index => $"Animals,Keyword {index},WORD{ToLetters(index)}")
        .ToArray();

    private static string ToLetters(int value)
    {
        var tens = (char)('A' + (value / 26));
        var ones = (char)('A' + (value % 26));
        return $"{tens}{ones}";
    }

    private static async Task<string> WriteTemporaryCsvAsync(string header, IReadOnlyList<string> rows)
    {
        var path = Path.Combine(Path.GetTempPath(), $"word-search-{Guid.NewGuid():N}.csv");
        await File.WriteAllLinesAsync(path, [header, .. rows], new UTF8Encoding(false));
        return path;
    }
}
