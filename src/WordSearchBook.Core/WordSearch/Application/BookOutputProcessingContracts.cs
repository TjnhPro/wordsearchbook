using WordSearchBook.Core.WordSearch.Contracts;

namespace WordSearchBook.Core.WordSearch.Application;

public sealed record PreparedBookAnswer(
    BookAnswerOutput Output,
    string PendingPath,
    string FinalPath);

public sealed record PreparedInteriorPdf(
    string PendingPath,
    string FinalPath,
    long LengthBytes,
    int PageCount);

public interface IBookAnswerBatchExporter
{
    Task<IReadOnlyList<PreparedBookAnswer>> PrepareAsync(
        string cacheDirectory,
        IReadOnlyList<GeneratedWordSearchTopic> topics,
        string outputDirectory,
        int maximumConcurrency,
        IProgress<BookProcessingProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public interface IBookInteriorPdfExporter
{
    Task<PreparedInteriorPdf> PrepareAsync(
        IReadOnlyList<string> orderedPages,
        string workDirectory,
        string pendingPath,
        string finalPath,
        int maximumConcurrency,
        IProgress<BookProcessingProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed record BookOutputPublicationRequest(
    string OutputDirectory,
    PreparedInteriorPdf Pdf,
    IReadOnlyList<PreparedBookAnswer> Answers,
    string PendingManifestPath,
    string ManifestPath);

public interface IBookOutputPublisher
{
    Task PublishAsync(
        BookOutputPublicationRequest request,
        CancellationToken cancellationToken = default);
}

public interface IBookProcessingSessionGate
{
    ValueTask<IAsyncDisposable?> TryAcquireAsync(
        string key,
        CancellationToken cancellationToken = default);
}
