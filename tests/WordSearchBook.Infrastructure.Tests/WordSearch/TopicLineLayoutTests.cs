using WordSearchBook.Infrastructure.WordSearch.Rendering;

namespace WordSearchBook.Infrastructure.Tests.WordSearch;

public sealed class TopicLineLayoutTests
{
    [Theory]
    [InlineData("BREATHE", "BREATHE")]
    [InlineData("COMFORTABLE CLOTHES", "COMFORTABLE", "CLOTHES")]
    [InlineData("WIND DOWN STRETCH", "WIND", "DOWN STRETCH")]
    [InlineData("REST YOUR BODY NOW", "REST YOUR", "BODY NOW")]
    [InlineData("CREATE A PEACEFUL BEDTIME ROUTINE", "CREATE A", "PEACEFUL BEDTIME ROUTINE")]
    public void SplitsTopicIntoAtMostTwoLinesUsingFloorCeilingWordCounts(
        string topicName,
        params string[] expected)
    {
        Assert.Equal(expected, TopicLineLayout.Split(topicName));
    }

    [Fact]
    public void CollapsesRepeatedSpacesWithoutDroppingWords()
    {
        Assert.Equal(
            ["WIND", "DOWN STRETCH"],
            TopicLineLayout.Split("WIND   DOWN  STRETCH"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectsEmptyTopic(string topicName)
    {
        Assert.Throws<ArgumentException>(() => TopicLineLayout.Split(topicName));
    }
}
