using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Core.WordSearch.Validation;

namespace WordSearchBook.Infrastructure.WordSearch.Validation;

public sealed class CsvBookDataValidationService(IBookDataValidationStateStore stateStore)
    : IBookDataValidationService
{
    internal const int SchemaVersion = 1;
    internal const int FingerprintFormatVersion = 1;
    internal const int RequiredEntriesPerTopic = 20;
    internal const int MaximumKeywordLength = 13;
    internal const int MaximumWordSearchKeyLength = 20;
    private static readonly string[] RequiredHeaders = ["TOPIC", "KEYWORD", "WORD SEARCH KEY"];

    public async ValueTask<BookDataValidationState> CheckStateAsync(
        string rootPath,
        string bookId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var record = await stateStore.LoadAsync(rootPath, bookId, cancellationToken);
            if (record is null)
            {
                return new BookDataValidationState(BookDataValidationStatus.NotValidated);
            }

            if (record.SchemaVersion != SchemaVersion || record.FingerprintFormatVersion != FingerprintFormatVersion)
            {
                return NeedsValidation(record, "book_data_validation_record_outdated");
            }

            var dataPath = JsonBookDataValidationStateStore.ResolveDataPath(rootPath, bookId);
            var fingerprint = CalculateMetadataFingerprint(CaptureMetadata(dataPath));
            if (!string.Equals(record.MetadataFingerprint, fingerprint, StringComparison.Ordinal))
            {
                return NeedsValidation(record, "book_data_fingerprint_changed");
            }

            return StateFromRecord(record);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or WordSearchGenerationException)
        {
            return new BookDataValidationState(
                BookDataValidationStatus.NeedsValidation,
                ReasonCode: exception is WordSearchGenerationException generationException
                    ? generationException.Code
                    : "book_data_validation_state_unavailable");
        }
    }

    public async ValueTask<BookDataValidationResult> ValidateAsync(
        string rootPath,
        string bookId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dataPath = JsonBookDataValidationStateStore.ResolveDataPath(rootPath, bookId);
        var before = CaptureMetadata(dataPath);
        byte[]? content = null;
        IReadOnlyList<BookDataTopicSummary> topics = [];
        IReadOnlyList<BookDataValidationFailure> failures;

        if (!before.Exists)
        {
            failures =
            [
                new BookDataValidationFailure(
                    "input_not_found",
                    $"Required CSV input was not found: {dataPath}")
            ];
        }
        else
        {
            try
            {
                content = await File.ReadAllBytesAsync(dataPath, cancellationToken);
                var parsed = Parse(content, cancellationToken);
                topics = parsed.Topics;
                failures = parsed.Failures;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new WordSearchGenerationException(
                    "input_read_failed",
                    $"CSV input could not be read: {exception.Message}",
                    exception);
            }
        }

        var after = CaptureMetadata(dataPath);
        if (before != after)
        {
            throw new WordSearchGenerationException(
                "book_data_changed_during_validation",
                "data.csv changed while it was being validated. Run validation again.");
        }

        var metadataFingerprint = CalculateMetadataFingerprint(after);
        var contentHash = content is null ? null : Hash(content);
        var validatedAt = DateTimeOffset.UtcNow;
        var record = new BookDataValidationRecord(
            SchemaVersion,
            FingerprintFormatVersion,
            metadataFingerprint,
            contentHash,
            validatedAt,
            failures.Count == 0,
            topics,
            failures);
        await stateStore.SaveAsync(rootPath, bookId, record, cancellationToken);

        var state = StateFromRecord(record);
        return new BookDataValidationResult(state, failures);
    }

    internal static string Hash(ReadOnlySpan<byte> content) =>
        $"sha256:{Convert.ToHexStringLower(SHA256.HashData(content))}";

    internal static string CalculateMetadataFingerprint(BookDataFileMetadata metadata)
    {
        var manifest = metadata.Exists
            ? $"fingerprintFormatVersion={FingerprintFormatVersion}\nfile=data.csv|length={metadata.Length}|lastWriteUtcTicks={metadata.LastWriteUtcTicks}"
            : $"fingerprintFormatVersion={FingerprintFormatVersion}\nfile=data.csv|missing";
        return Hash(Encoding.UTF8.GetBytes(manifest));
    }

    internal static BookDataFileMetadata CaptureMetadata(string path)
    {
        var file = new FileInfo(path);
        file.Refresh();
        return file.Exists
            ? new BookDataFileMetadata(true, file.Length, file.LastWriteTimeUtc.Ticks)
            : new BookDataFileMetadata(false, 0, 0);
    }

    private static ParsedValidation Parse(byte[] content, CancellationToken cancellationToken)
    {
        var failures = new List<BookDataValidationFailure>();
        var topicBuilders = new List<TopicBuilder>();
        var topicsByName = new Dictionary<string, TopicBuilder>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var stream = new MemoryStream(content, writable: false);
            using var reader = new StreamReader(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
                detectEncodingFromByteOrderMarks: true);
            using var csv = new CsvReader(reader, CreateConfiguration());
            if (!csv.Read())
            {
                failures.Add(new BookDataValidationFailure("csv_empty", "CSV input does not contain a header row."));
                return new ParsedValidation([], failures);
            }

            csv.ReadHeader();
            var available = (csv.HeaderRecord ?? [])
                .Select(header => header.Trim().ToUpperInvariant())
                .ToHashSet(StringComparer.Ordinal);
            var missing = RequiredHeaders.Where(header => !available.Contains(header)).ToArray();
            if (missing.Length > 0)
            {
                failures.Add(new BookDataValidationFailure(
                    "csv_header_missing",
                    $"CSV input is missing required header(s): {string.Join(", ", missing)}."));
                return new ParsedValidation([], failures);
            }

            while (csv.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sourceRow = csv.Context.Parser?.Row ?? 0;
                var topic = csv.GetField("Topic")?.Trim();
                var keyword = csv.GetField("Keyword")?.Trim();
                var rawKey = csv.GetField("Word Search Key") ?? string.Empty;
                var normalizedTopic = string.IsNullOrWhiteSpace(topic) ? null : topic.ToUpperInvariant();

                TopicBuilder? builder = null;
                if (normalizedTopic is null)
                {
                    failures.Add(new BookDataValidationFailure(
                        "topic_invalid",
                        $"CSV row {sourceRow}: Topic cannot be empty.",
                        sourceRow));
                }
                else
                {
                    if (!topicsByName.TryGetValue(normalizedTopic, out builder))
                    {
                        builder = new TopicBuilder(normalizedTopic);
                        topicsByName.Add(normalizedTopic, builder);
                        topicBuilders.Add(builder);
                    }

                    builder.RowCount++;
                }

                if (string.IsNullOrWhiteSpace(keyword))
                {
                    AddTopicFailure(failures, builder, "keyword_invalid", $"CSV row {sourceRow}: Keyword cannot be empty.", sourceRow);
                }
                else
                {
                    var compactKeywordLength = keyword.Count(character => !char.IsWhiteSpace(character));
                    if (compactKeywordLength > MaximumKeywordLength)
                    {
                        AddTopicFailure(
                            failures,
                            builder,
                            "keyword_too_long",
                            $"CSV row {sourceRow}: Keyword '{keyword}' exceeds {MaximumKeywordLength} characters when whitespace is ignored.",
                            sourceRow);
                    }
                }

                var normalizedKey = new string(rawKey.Where(character => !char.IsWhiteSpace(character)).ToArray())
                    .ToUpperInvariant();
                if (normalizedKey.Length == 0 || normalizedKey.Any(character => character is < 'A' or > 'Z'))
                {
                    AddTopicFailure(
                        failures,
                        builder,
                        "word_invalid",
                        $"CSV row {sourceRow}: Word Search Key must contain only letters A-Z after whitespace is removed.",
                        sourceRow);
                }
                else if (normalizedKey.Length > MaximumWordSearchKeyLength)
                {
                    AddTopicFailure(
                        failures,
                        builder,
                        "word_too_long",
                        $"CSV row {sourceRow}: Word Search Key '{normalizedKey}' exceeds {MaximumWordSearchKeyLength} letters.",
                        sourceRow);
                }
                else if (builder is not null && !builder.WordSearchKeys.Add(normalizedKey))
                {
                    AddTopicFailure(
                        failures,
                        builder,
                        "duplicate_word",
                        $"CSV row {sourceRow}: Word Search Key '{normalizedKey}' is duplicated in Topic '{builder.Name}'.",
                        sourceRow);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is CsvHelperException or DecoderFallbackException)
        {
            failures.Add(new BookDataValidationFailure("csv_invalid", $"CSV input is invalid: {exception.Message}"));
        }

        if (topicBuilders.Count == 0 && failures.Count == 0)
        {
            failures.Add(new BookDataValidationFailure("csv_empty", "CSV input does not contain any data rows."));
        }

        foreach (var topic in topicBuilders)
        {
            if (topic.RowCount != RequiredEntriesPerTopic)
            {
                topic.HasFailure = true;
                failures.Add(new BookDataValidationFailure(
                    "topic_word_count_invalid",
                    $"Topic '{topic.Name}' must contain exactly {RequiredEntriesPerTopic} entries but contains {topic.RowCount}.",
                    Topic: topic.Name));
            }
        }

        var summaries = topicBuilders
            .Select(topic => new BookDataTopicSummary(topic.Name, topic.RowCount, !topic.HasFailure))
            .ToArray();
        return new ParsedValidation(summaries, failures);
    }

    private static CsvConfiguration CreateConfiguration() => new(CultureInfo.InvariantCulture)
    {
        PrepareHeaderForMatch = args => args.Header.Trim().ToUpperInvariant()
    };

    private static void AddTopicFailure(
        ICollection<BookDataValidationFailure> failures,
        TopicBuilder? builder,
        string code,
        string message,
        int sourceRow)
    {
        if (builder is not null)
        {
            builder.HasFailure = true;
        }

        failures.Add(new BookDataValidationFailure(code, message, sourceRow, builder?.Name));
    }

    private static BookDataValidationState StateFromRecord(BookDataValidationRecord record) => new(
        record.IsValid ? BookDataValidationStatus.Validated : BookDataValidationStatus.Invalid,
        record.ValidatedAtUtc,
        record.MetadataFingerprint,
        record.ContentHash,
        record.IsValid ? null : record.Failures.FirstOrDefault()?.Code,
        record.Topics,
        record.Failures);

    private static BookDataValidationState NeedsValidation(BookDataValidationRecord record, string reasonCode) => new(
        BookDataValidationStatus.NeedsValidation,
        record.ValidatedAtUtc,
        record.MetadataFingerprint,
        record.ContentHash,
        reasonCode,
        record.Topics,
        record.Failures);

    internal sealed record BookDataFileMetadata(bool Exists, long Length, long LastWriteUtcTicks);

    private sealed record ParsedValidation(
        IReadOnlyList<BookDataTopicSummary> Topics,
        IReadOnlyList<BookDataValidationFailure> Failures);

    private sealed class TopicBuilder(string name)
    {
        public string Name { get; } = name;

        public int RowCount { get; set; }

        public bool HasFailure { get; set; }

        public HashSet<string> WordSearchKeys { get; } = new(StringComparer.Ordinal);
    }
}
