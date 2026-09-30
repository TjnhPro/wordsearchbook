using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using System.Text;
using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Core.WordSearch.Domain;
using WordSearchBook.Core.WordSearch.Input;

namespace WordSearchBook.Infrastructure.WordSearch.Input;

public sealed class CsvWordSearchInputReader : IWordSearchInputReader
{
    private const int RequiredEntriesPerTopic = 20;
    private const int MaximumKeywordLength = 13;
    private const int MaximumWordLength = 20;
    private static readonly string[] RequiredHeaders = ["TOPIC", "KEYWORD", "WORD SEARCH KEY"];

    public async Task<IReadOnlyList<WordSearchTopic>> ReadAsync(
        string dataCsvPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataCsvPath);

        if (!File.Exists(dataCsvPath))
        {
            throw new WordSearchGenerationException("input_not_found", $"CSV input was not found: {dataCsvPath}");
        }

        try
        {
            using var streamReader = new StreamReader(
                dataCsvPath,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
                detectEncodingFromByteOrderMarks: true);
            using var csv = new CsvReader(streamReader, CreateConfiguration());

            if (!await csv.ReadAsync())
            {
                throw new WordSearchGenerationException("csv_empty", "CSV input does not contain a header row.");
            }

            csv.ReadHeader();
            ValidateHeaders(csv.HeaderRecord);

            var topicBuilders = new List<TopicBuilder>();
            var topicsByName = new Dictionary<string, TopicBuilder>(StringComparer.OrdinalIgnoreCase);

            while (await csv.ReadAsync())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sourceRow = csv.Context.Parser?.Row ?? 0;
                var topicName = RequireUppercase(csv.GetField("Topic"), "Topic", sourceRow);
                var keyword = RequireUppercase(csv.GetField("Keyword"), "Keyword", sourceRow);
                if (keyword.Count(character => !char.IsWhiteSpace(character)) > MaximumKeywordLength)
                {
                    throw new WordSearchGenerationException(
                        "keyword_too_long",
                        $"CSV row {sourceRow}: Keyword '{keyword}' exceeds {MaximumKeywordLength} characters when whitespace is ignored.");
                }
                var wordSearchKey = NormalizeWordSearchKey(csv.GetField("Word Search Key"), sourceRow);

                if (!topicsByName.TryGetValue(topicName, out var builder))
                {
                    builder = new TopicBuilder(topicBuilders.Count + 1, topicName);
                    topicsByName.Add(topicName, builder);
                    topicBuilders.Add(builder);
                }

                if (!builder.WordSearchKeys.Add(wordSearchKey))
                {
                    throw new WordSearchGenerationException(
                        "duplicate_word",
                        $"CSV row {sourceRow}: Word Search Key '{wordSearchKey}' is duplicated in Topic '{builder.Name}'.");
                }

                builder.Entries.Add(new WordSearchEntry(sourceRow, keyword, wordSearchKey));
            }

            if (topicBuilders.Count == 0)
            {
                throw new WordSearchGenerationException("csv_empty", "CSV input does not contain any data rows.");
            }

            foreach (var topic in topicBuilders)
            {
                if (topic.Entries.Count != RequiredEntriesPerTopic)
                {
                    throw new WordSearchGenerationException(
                        "topic_word_count_invalid",
                        $"Topic '{topic.Name}' must contain exactly {RequiredEntriesPerTopic} entries but contains {topic.Entries.Count}.");
                }
            }

            return topicBuilders
                .Select(builder => new WordSearchTopic(builder.Index, builder.Name, builder.Entries.ToArray()))
                .ToArray();
        }
        catch (WordSearchGenerationException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is CsvHelperException or DecoderFallbackException)
        {
            throw new WordSearchGenerationException("csv_invalid", $"CSV input is invalid: {exception.Message}", exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new WordSearchGenerationException("input_read_failed", $"CSV input could not be read: {exception.Message}", exception);
        }
    }

    private static CsvConfiguration CreateConfiguration() => new(CultureInfo.InvariantCulture)
    {
        PrepareHeaderForMatch = args => args.Header.Trim().ToUpperInvariant()
    };

    private static void ValidateHeaders(string[]? headers)
    {
        var available = (headers ?? [])
            .Select(header => header.Trim().ToUpperInvariant())
            .ToHashSet(StringComparer.Ordinal);
        var missing = RequiredHeaders.Where(header => !available.Contains(header)).ToArray();

        if (missing.Length > 0)
        {
            throw new WordSearchGenerationException(
                "csv_header_missing",
                $"CSV input is missing required header(s): {string.Join(", ", missing)}.");
        }
    }

    private static string RequireUppercase(string? value, string fieldName, int sourceRow)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new WordSearchGenerationException(
                fieldName == "Topic" ? "topic_invalid" : "keyword_invalid",
                $"CSV row {sourceRow}: {fieldName} cannot be empty.");
        }

        return trimmed.ToUpperInvariant();
    }

    private static string NormalizeWordSearchKey(string? value, int sourceRow)
    {
        var normalized = new string((value ?? string.Empty)
            .Where(character => !char.IsWhiteSpace(character))
            .ToArray())
            .ToUpperInvariant();

        if (normalized.Length == 0 || normalized.Any(character => character is < 'A' or > 'Z'))
        {
            throw new WordSearchGenerationException(
                "word_invalid",
                $"CSV row {sourceRow}: Word Search Key must contain only letters A-Z after whitespace is removed.");
        }

        if (normalized.Length > MaximumWordLength)
        {
            throw new WordSearchGenerationException(
                "word_too_long",
                $"CSV row {sourceRow}: Word Search Key '{normalized}' exceeds {MaximumWordLength} letters.");
        }

        return normalized;
    }

    private sealed class TopicBuilder(int index, string name)
    {
        public int Index { get; } = index;

        public string Name { get; } = name;

        public List<WordSearchEntry> Entries { get; } = [];

        public HashSet<string> WordSearchKeys { get; } = new(StringComparer.Ordinal);
    }
}
