using WordSearchBook.Core.WordSearch.Domain;

namespace WordSearchBook.Core.WordSearch.Application;

public interface IBrandPagePreviewService
{
    Task<BrandPagePreviewResult> DrawAsync(
        string rootPath,
        string brandId,
        CancellationToken cancellationToken = default);
}

public sealed record BrandPagePreviewResult(
    string BrandId,
    string FileName,
    int Width,
    int Height,
    DateTimeOffset GeneratedAtUtc);

public static class BrandPagePreviewSample
{
    public const string OutputFileName = "page_layout.preview.png";
    public const string TopicName = "AMAZING ANIMALS";
    public const int PageNumber = 1;

    public static readonly IReadOnlyList<string> Keywords =
    [
        "RED PANDA",
        "BALD EAGLE",
        "BLUE WHALE",
        "POLAR BEAR",
        "SNOW LEOPARD",
        "SEA TURTLE",
        "GIANT PANDA",
        "GRAY WOLF",
        "BROWN BEAR",
        "WILD HORSE",
        "KING COBRA",
        "TIGER SHARK",
        "GOLDEN EAGLE",
        "BLACK SWAN",
        "RIVER OTTER",
        "MOUNTAIN GOAT",
        "DESERT FOX",
        "GREEN TURTLE",
        "WHITE TIGER",
        "CORAL SNAKE"
    ];

    public static WordSearchTopic CreateTopic() => new(
        PageNumber,
        TopicName,
        Keywords
            .Select((keyword, index) => new WordSearchEntry(
                index + 1,
                keyword,
                keyword.Replace(" ", string.Empty, StringComparison.Ordinal)))
            .ToArray());
}
