using System.Collections.Concurrent;
using WordSearchBook.Core.WordSearch.Application;

namespace WordSearchBook.Infrastructure.WordSearch.Processing;

public sealed class BookProcessingSessionGate : IBookProcessingSessionGate, IDisposable
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> gates = new(StringComparer.OrdinalIgnoreCase);

    public async ValueTask<IAsyncDisposable?> TryAcquireAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var gate = gates.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        return await gate.WaitAsync(0, cancellationToken) ? new Lease(gate) : null;
    }

    public void Dispose()
    {
        foreach (var gate in gates.Values)
        {
            gate.Dispose();
        }
    }

    private sealed class Lease(SemaphoreSlim gate) : IAsyncDisposable
    {
        private int released;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref released, 1) == 0)
            {
                gate.Release();
            }

            return ValueTask.CompletedTask;
        }
    }
}
