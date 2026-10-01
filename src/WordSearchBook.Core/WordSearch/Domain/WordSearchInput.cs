namespace WordSearchBook.Core.WordSearch.Domain;

public sealed record WordSearchEntry(
    int SourceRow,
    string Keyword,
    string WordSearchKey);

public sealed record WordSearchTopic(
    int Index,
    string Name,
    string Quote,
    IReadOnlyList<WordSearchEntry> Entries);

public static class QuoteText
{
    public static string Normalize(string? value) => string.Join(
        ' ',
        (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
