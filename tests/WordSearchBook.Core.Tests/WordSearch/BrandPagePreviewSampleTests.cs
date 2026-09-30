using WordSearchBook.Core.WordSearch.Application;

namespace WordSearchBook.Core.Tests.WordSearch;

public sealed class BrandPagePreviewSampleTests
{
    [Fact]
    public void CreatesFixedUppercaseTwentyKeywordTopic()
    {
        var topic = BrandPagePreviewSample.CreateTopic();

        Assert.Equal("AMAZING ANIMALS", topic.Name);
        Assert.Equal(1, topic.Index);
        Assert.Equal(20, topic.Entries.Count);
        Assert.Equal("RED PANDA", topic.Entries[0].Keyword);
        Assert.Equal("REDPANDA", topic.Entries[0].WordSearchKey);
        Assert.Equal("CORAL SNAKE", topic.Entries[^1].Keyword);
        Assert.Equal("CORALSNAKE", topic.Entries[^1].WordSearchKey);
        Assert.All(topic.Entries, entry =>
        {
            Assert.Equal(entry.Keyword.ToUpperInvariant(), entry.Keyword);
            Assert.DoesNotContain(' ', entry.WordSearchKey);
        });
    }
}
