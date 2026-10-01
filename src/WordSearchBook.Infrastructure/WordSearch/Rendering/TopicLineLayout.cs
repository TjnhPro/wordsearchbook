namespace WordSearchBook.Infrastructure.WordSearch.Rendering;

internal static class TopicLineLayout
{
    internal static IReadOnlyList<string> Split(string topicName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topicName);

        var words = topicName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 1)
        {
            return words;
        }

        var firstLineWordCount = words.Length / 2;
        return
        [
            string.Join(' ', words[..firstLineWordCount]),
            string.Join(' ', words[firstLineWordCount..])
        ];
    }
}
