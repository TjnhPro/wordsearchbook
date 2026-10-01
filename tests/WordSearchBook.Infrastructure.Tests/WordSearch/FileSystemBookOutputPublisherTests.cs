using WordSearchBook.Core.WordSearch.Application;
using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Infrastructure.WordSearch.Processing;

namespace WordSearchBook.Infrastructure.Tests.WordSearch;

public sealed class FileSystemBookOutputPublisherTests
{
    [Fact]
    public async Task OverwritesIndividualOutputsAndRemovesObsoleteAnswers()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var output = Path.Combine(root, "output");
            var answers = Path.Combine(output, "answer");
            var workspace = Path.Combine(root, ".workspace");
            Directory.CreateDirectory(answers);
            Directory.CreateDirectory(workspace);
            var finalPdf = Path.Combine(output, "book.interior.pdf");
            var pendingPdf = Path.Combine(output, ".book.interior.pdf.pending");
            var finalAnswer = Path.Combine(answers, "001.jpg");
            var pendingAnswer = Path.Combine(answers, ".001.jpg.pending");
            var staleAnswer = Path.Combine(answers, "999.jpg");
            var manifest = Path.Combine(workspace, "output.manifest.json");
            var pendingManifest = Path.Combine(workspace, "output.manifest.pending.json");
            await File.WriteAllTextAsync(finalPdf, "old-pdf");
            await File.WriteAllTextAsync(finalAnswer, "old-answer");
            await File.WriteAllTextAsync(staleAnswer, "stale");
            await File.WriteAllTextAsync(manifest, "old-manifest");
            await File.WriteAllTextAsync(pendingPdf, "new-pdf");
            await File.WriteAllTextAsync(pendingAnswer, "new-answer");
            await File.WriteAllTextAsync(pendingManifest, "new-manifest");

            await new FileSystemBookOutputPublisher().PublishAsync(new BookOutputPublicationRequest(
                output,
                new PreparedInteriorPdf(pendingPdf, finalPdf, 7, 1),
                [new PreparedBookAnswer(new BookAnswerOutput(1, "answer/001.jpg", 10, 2588, 3375, 85), pendingAnswer, finalAnswer)],
                pendingManifest,
                manifest));

            Assert.Equal("new-pdf", await File.ReadAllTextAsync(finalPdf));
            Assert.Equal("new-answer", await File.ReadAllTextAsync(finalAnswer));
            Assert.Equal("new-manifest", await File.ReadAllTextAsync(manifest));
            Assert.False(File.Exists(staleAnswer));
            Assert.False(File.Exists(pendingPdf));
            Assert.False(File.Exists(pendingAnswer));
            Assert.False(File.Exists(pendingManifest));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LockedOutputFailsBeforeReplacingAnyFinalFile()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var output = Path.Combine(root, "output");
            var answers = Path.Combine(output, "answer");
            var workspace = Path.Combine(root, ".workspace");
            Directory.CreateDirectory(answers);
            Directory.CreateDirectory(workspace);
            var finalPdf = Path.Combine(output, "book.interior.pdf");
            var pendingPdf = Path.Combine(output, ".book.interior.pdf.pending");
            var finalAnswer = Path.Combine(answers, "001.jpg");
            var pendingAnswer = Path.Combine(answers, ".001.jpg.pending");
            var manifest = Path.Combine(workspace, "output.manifest.json");
            var pendingManifest = Path.Combine(workspace, "output.manifest.pending.json");
            await File.WriteAllTextAsync(finalPdf, "old-pdf");
            await File.WriteAllTextAsync(finalAnswer, "old-answer");
            await File.WriteAllTextAsync(manifest, "old-manifest");
            await File.WriteAllTextAsync(pendingPdf, "new-pdf");
            await File.WriteAllTextAsync(pendingAnswer, "new-answer");
            await File.WriteAllTextAsync(pendingManifest, "new-manifest");

            WordSearchGenerationException exception;
            await using (var locked = new FileStream(finalPdf, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                exception = await Assert.ThrowsAsync<WordSearchGenerationException>(() =>
                    new FileSystemBookOutputPublisher().PublishAsync(new BookOutputPublicationRequest(
                        output,
                        new PreparedInteriorPdf(pendingPdf, finalPdf, 7, 1),
                        [new PreparedBookAnswer(new BookAnswerOutput(1, "answer/001.jpg", 10, 2588, 3375, 85), pendingAnswer, finalAnswer)],
                        pendingManifest,
                        manifest)));
            }

            Assert.Equal("output_file_in_use", exception.Code);
            Assert.Contains(finalPdf, exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("old-pdf", await File.ReadAllTextAsync(finalPdf));
            Assert.Equal("old-answer", await File.ReadAllTextAsync(finalAnswer));
            Assert.Equal("old-manifest", await File.ReadAllTextAsync(manifest));
            Assert.True(File.Exists(pendingAnswer));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SessionGateRejectsConcurrentProcessingForSameBook()
    {
        using var gate = new BookProcessingSessionGate();
        var first = await gate.TryAcquireAsync("root|book");
        var duplicate = await gate.TryAcquireAsync("root|book");

        Assert.NotNull(first);
        Assert.Null(duplicate);
        await first!.DisposeAsync();
        var next = await gate.TryAcquireAsync("root|book");
        Assert.NotNull(next);
        await next!.DisposeAsync();
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"word-search-output-publisher-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
