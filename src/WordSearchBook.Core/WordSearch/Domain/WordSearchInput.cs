namespace WordSearchBook.Core.WordSearch.Domain;

public sealed record WordSearchEntry(
    int SourceRow,
    string Keyword,
    string WordSearchKey);

public sealed record WordSearchTopic(
    int Index,
    string Name,
    IReadOnlyList<WordSearchEntry> Entries);
