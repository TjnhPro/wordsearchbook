using WordSearchBook.Core.WordSearch.Caching;
using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Core.WordSearch.Domain;
using WordSearchBook.Core.WordSearch.Generation;
using WordSearchBook.Core.WordSearch.Rendering;

namespace WordSearchBook.Core.WordSearch.Application;

public interface IWordSearchTopicProcessor
{
    WordSearchTopicArtifactSet Process(
        WordSearchTopic topic,
        string pageLayoutPath,
        string frontLayoutPath,
        WordSearchSettingsBundle settings);
}

public sealed class WordSearchTopicProcessor(
    IWordSearchPuzzleGenerator puzzleGenerator,
    IWordSearchBoardRenderer boardRenderer,
    IWordSearchPageRenderer pageRenderer) : IWordSearchTopicProcessor
{
    public WordSearchTopicArtifactSet Process(
        WordSearchTopic topic,
        string pageLayoutPath,
        string frontLayoutPath,
        WordSearchSettingsBundle settings)
    {
        ArgumentNullException.ThrowIfNull(topic);
        ArgumentException.ThrowIfNullOrWhiteSpace(pageLayoutPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(frontLayoutPath);
        ArgumentNullException.ThrowIfNull(settings);

        var puzzle = puzzleGenerator.Generate(
            topic.Entries.Select(entry => entry.WordSearchKey).ToArray(),
            settings.Global.Board);
        var board = boardRenderer.RenderData(puzzle, settings.Global.Board, settings.Brand.BoardGame);
        var answerBoard = boardRenderer.RenderAnswer(
            puzzle,
            settings.Global.Board,
            settings.Brand.BoardGame,
            settings.Brand.AnswerLine);
        RenderedWordSearchArtifact[] artifacts =
        [
            board,
            answerBoard,
            pageRenderer.Render(pageLayoutPath, frontLayoutPath, topic, topic.Index, board, settings, WordSearchArtifactKind.Page),
            pageRenderer.Render(pageLayoutPath, frontLayoutPath, topic, topic.Index, answerBoard, settings, WordSearchArtifactKind.PageAnswer)
        ];
        return new WordSearchTopicArtifactSet(topic, artifacts, puzzle.Placements);
    }
}

public interface IWordSearchTopicBatchProcessor
{
    Task<IReadOnlyList<GeneratedWordSearchTopic>> ProcessAsync(
        IReadOnlyList<WordSearchTopic> topics,
        string pageLayoutPath,
        string frontLayoutPath,
        WordSearchSettingsBundle settings,
        IWordSearchCacheSession cacheSession,
        IProgress<WordSearchGenerationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class BoundedWordSearchTopicBatchProcessor(IWordSearchTopicProcessor topicProcessor)
    : IWordSearchTopicBatchProcessor
{
    public async Task<IReadOnlyList<GeneratedWordSearchTopic>> ProcessAsync(
        IReadOnlyList<WordSearchTopic> topics,
        string pageLayoutPath,
        string frontLayoutPath,
        WordSearchSettingsBundle settings,
        IWordSearchCacheSession cacheSession,
        IProgress<WordSearchGenerationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(topics);
        ArgumentException.ThrowIfNullOrWhiteSpace(pageLayoutPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(frontLayoutPath);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(cacheSession);
        var duplicate = topics.GroupBy(topic => topic.Index).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new WordSearchGenerationException(
                "topic_work_key_duplicate",
                $"Topic work key 'topic:{duplicate.Key}' is duplicated.");
        }

        using var semaphore = new SemaphoreSlim(
            settings.Global.MaximumProcessingConcurrency,
            settings.Global.MaximumProcessingConcurrency);
        using var remainingWorkCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var results = new GeneratedWordSearchTopic?[topics.Count];
        var completed = 0;
        var work = topics.Select((topic, position) => Task.Run(
            () => ProcessOneAsync(topic, position),
            CancellationToken.None)).ToArray();

        try
        {
            await Task.WhenAll(work);
        }
        catch
        {
            await remainingWorkCancellation.CancelAsync();
            try
            {
                await Task.WhenAll(work);
            }
            catch
            {
                // Preserve the first worker failure observed by the caller.
            }

            throw;
        }

        return results.Select(result => result!).ToArray();

        async Task ProcessOneAsync(WordSearchTopic topic, int position)
        {
            var entered = false;
            try
            {
                await semaphore.WaitAsync(remainingWorkCancellation.Token);
                entered = true;
                remainingWorkCancellation.Token.ThrowIfCancellationRequested();
                var artifacts = topicProcessor.Process(topic, pageLayoutPath, frontLayoutPath, settings);
                results[position] = await cacheSession.PublishTopicAsync(
                    artifacts,
                    remainingWorkCancellation.Token);
                var count = Interlocked.Increment(ref completed);
                progress?.Report(new WordSearchGenerationProgress(count, topics.Count, topic.Index, topic.Name));
            }
            catch (OperationCanceledException)
            {
                await remainingWorkCancellation.CancelAsync();
                throw;
            }
            catch (WordSearchGenerationException exception)
            {
                await remainingWorkCancellation.CancelAsync();
                throw new WordSearchGenerationException(
                    exception.Code,
                    $"Topic {topic.Index} '{topic.Name}': {exception.Message}",
                    exception);
            }
            catch
            {
                await remainingWorkCancellation.CancelAsync();
                throw;
            }
            finally
            {
                if (entered)
                {
                    semaphore.Release();
                }
            }
        }
    }
}
