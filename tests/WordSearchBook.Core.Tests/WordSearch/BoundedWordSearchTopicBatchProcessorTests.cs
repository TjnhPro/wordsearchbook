using WordSearchBook.Core.WordSearch.Application;
using WordSearchBook.Core.WordSearch.Caching;
using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Core.WordSearch.Domain;

namespace WordSearchBook.Core.Tests.WordSearch;

public sealed class BoundedWordSearchTopicBatchProcessorTests
{
    [Fact]
    public async Task ProcessesTopicsWithinLimitAndReturnsCsvOrder()
    {
        var processor = new ControllableTopicProcessor(expectedConcurrency: 2);
        var batch = new BoundedWordSearchTopicBatchProcessor(processor);
        var topics = new[] { Topic(1), Topic(2), Topic(3) };
        await using var cache = new RecordingCacheSession();
        var settings = new WordSearchSettingsBundle(
            WordSearchSettingsDefaults.CreateGlobal() with { MaximumProcessingConcurrency = 2 },
            WordSearchSettingsDefaults.CreateBrand());

        var results = await batch.ProcessAsync(topics, "page-layout.png", "front-layout.png", settings, cache);

        Assert.Equal(2, processor.MaximumObservedConcurrency);
        Assert.Equal([1, 2, 3], results.Select(result => result.Index));
        Assert.Equal([1, 2, 3], cache.PublishedIndexes.Order());
    }

    [Fact]
    public async Task RejectsDuplicateTopicWorkKeysBeforeStartingWorkers()
    {
        var processor = new ControllableTopicProcessor(expectedConcurrency: 1);
        var batch = new BoundedWordSearchTopicBatchProcessor(processor);
        await using var cache = new RecordingCacheSession();

        var exception = await Assert.ThrowsAsync<WordSearchGenerationException>(() => batch.ProcessAsync(
            [Topic(1), Topic(1)],
            "page-layout.png",
            "front-layout.png",
            new WordSearchSettingsBundle(WordSearchSettingsDefaults.CreateGlobal(), WordSearchSettingsDefaults.CreateBrand()),
            cache));

        Assert.Equal("topic_work_key_duplicate", exception.Code);
        Assert.Equal(0, processor.StartedCount);
    }

    private static WordSearchTopic Topic(int index) => new(index, $"TOPIC {index}", []);

    private sealed class ControllableTopicProcessor(int expectedConcurrency) : IWordSearchTopicProcessor
    {
        private readonly ManualResetEventSlim release = new(initialState: false);
        private int active;
        private int maximumObservedConcurrency;
        private int startedCount;

        public int MaximumObservedConcurrency => Volatile.Read(ref maximumObservedConcurrency);

        public int StartedCount => Volatile.Read(ref startedCount);

        public WordSearchTopicArtifactSet Process(
            WordSearchTopic topic,
            string pageLayoutPath,
            string frontLayoutPath,
            WordSearchSettingsBundle settings)
        {
            Interlocked.Increment(ref startedCount);
            var current = Interlocked.Increment(ref active);
            UpdateMaximum(current);
            if (current >= expectedConcurrency)
            {
                release.Set();
            }

            try
            {
                Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
                return new WordSearchTopicArtifactSet(topic, [], []);
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        }

        private void UpdateMaximum(int candidate)
        {
            var observed = Volatile.Read(ref maximumObservedConcurrency);
            while (candidate > observed)
            {
                var previous = Interlocked.CompareExchange(ref maximumObservedConcurrency, candidate, observed);
                if (previous == observed) return;
                observed = previous;
            }
        }
    }

    private sealed class RecordingCacheSession : IWordSearchCacheSession
    {
        private readonly List<int> publishedIndexes = [];

        public IReadOnlyList<int> PublishedIndexes
        {
            get
            {
                lock (publishedIndexes)
                {
                    return publishedIndexes.ToArray();
                }
            }
        }

        public ValueTask<GeneratedWordSearchTopic> PublishTopicAsync(
            WordSearchTopicArtifactSet topic,
            CancellationToken cancellationToken = default)
        {
            lock (publishedIndexes)
            {
                publishedIndexes.Add(topic.Topic.Index);
            }

            return ValueTask.FromResult(new GeneratedWordSearchTopic(
                topic.Topic.Index,
                topic.Topic.Name,
                [],
                topic.Placements));
        }

        public ValueTask<WordSearchGenerationResult> CommitAsync(
            IReadOnlyList<GeneratedWordSearchTopic> topics,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
