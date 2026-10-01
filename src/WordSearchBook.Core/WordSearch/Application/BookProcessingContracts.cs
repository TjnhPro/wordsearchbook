namespace WordSearchBook.Core.WordSearch.Application;

public sealed record BookProcessingRequest(
    string RootPath,
    string BookId,
    string BrandId);

public sealed record BookProcessingProgress(
    string Step,
    int? Completed = null,
    int? Total = null,
    string? Detail = null);

public sealed record BookAnswerOutput(
    int TopicIndex,
    string RelativePath,
    long LengthBytes,
    int Width,
    int Height,
    int Quality);

public sealed record BookProcessingResult(
    string BookId,
    string BrandId,
    string PdfPath,
    string AnswerDirectory,
    int PuzzlePageCount,
    int FrontPageCount,
    int BackPageCount,
    int PdfPageCount,
    long PdfLengthBytes,
    IReadOnlyList<BookAnswerOutput> Answers,
    DateTimeOffset ProcessedAtUtc);

public interface IBookProcessingService
{
    Task<BookProcessingResult> ProcessAsync(
        BookProcessingRequest request,
        IProgress<BookProcessingProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
