using WordSearchBook.Core.WordSearch.Domain;

namespace WordSearchBook.Core.WordSearch.Validation;

public enum BookDataValidationStatus
{
    NotValidated,
    Validated,
    Invalid,
    NeedsValidation
}

public sealed record BookDataTopicSummary(
    string Name,
    int KeywordCount,
    bool IsValid);

public sealed record BookDataValidationFailure(
    string Code,
    string Message,
    int? SourceRow = null,
    string? Topic = null);

public sealed record BookDataValidationRecord(
    int SchemaVersion,
    int FingerprintFormatVersion,
    int MaximumKeywordLength,
    string MetadataFingerprint,
    string? ContentHash,
    DateTimeOffset ValidatedAtUtc,
    bool IsValid,
    IReadOnlyList<BookDataTopicSummary> Topics,
    IReadOnlyList<BookDataValidationFailure> Failures);

public sealed record BookDataValidationState(
    BookDataValidationStatus Status,
    DateTimeOffset? ValidatedAtUtc = null,
    string? MetadataFingerprint = null,
    string? ContentHash = null,
    string? ReasonCode = null,
    IReadOnlyList<BookDataTopicSummary>? Topics = null,
    IReadOnlyList<BookDataValidationFailure>? Failures = null)
{
    public int TopicCount => Topics?.Count ?? 0;

    public int KeywordCount => Topics?.Sum(topic => topic.KeywordCount) ?? 0;
}

public sealed record BookDataValidationResult(
    BookDataValidationState State,
    IReadOnlyList<BookDataValidationFailure> Failures)
{
    public bool IsSuccess => State.Status == BookDataValidationStatus.Validated && Failures.Count == 0;
}

public interface IBookDataValidationService
{
    ValueTask<BookDataValidationState> CheckStateAsync(
        string rootPath,
        string bookId,
        int maximumKeywordLength = WordSearchSettingsDefaults.MaximumKeywordLength,
        CancellationToken cancellationToken = default);

    ValueTask<BookDataValidationResult> ValidateAsync(
        string rootPath,
        string bookId,
        int maximumKeywordLength = WordSearchSettingsDefaults.MaximumKeywordLength,
        CancellationToken cancellationToken = default);
}

public interface IBookDataValidationStateStore
{
    ValueTask<BookDataValidationRecord?> LoadAsync(
        string rootPath,
        string bookId,
        CancellationToken cancellationToken = default);

    ValueTask SaveAsync(
        string rootPath,
        string bookId,
        BookDataValidationRecord record,
        CancellationToken cancellationToken = default);
}
