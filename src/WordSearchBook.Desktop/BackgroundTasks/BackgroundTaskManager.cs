using Microsoft.Extensions.DependencyInjection;
using WordSearchBook.Core.Application.BackgroundTasks;

namespace WordSearchBook.Desktop.BackgroundTasks;

public sealed class BackgroundTaskManager(IServiceProvider serviceProvider) : IBackgroundTaskManager, IDisposable
{
    private const int MaximumTerminalHistory = 100;
    private readonly Lock sync = new();
    private readonly Dictionary<BackgroundTaskId, Entry> registry = [];
    private readonly Queue<BackgroundTaskId> queue = [];
    private readonly Queue<BackgroundTaskId> terminalOrder = [];
    private BackgroundTaskId? activeTaskId;
    private long nextSequence;
    private bool disposed;

    public ValueTask<BackgroundTaskSnapshot> StartAsync<TRequest>(
        BackgroundTaskKind kind,
        string key,
        string? subject,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(request);

        Entry entry;
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var duplicate = registry.Values.FirstOrDefault(candidate =>
                candidate.Kind == kind &&
                string.Equals(candidate.Key, key, StringComparison.Ordinal) &&
                !IsTerminal(candidate.State));
            if (duplicate is not null)
            {
                return ValueTask.FromResult(SnapshotLocked(duplicate));
            }

            entry = new Entry
            {
                TaskId = BackgroundTaskId.New(),
                Kind = kind,
                Key = key,
                Subject = subject,
                Request = request,
                State = BackgroundTaskState.Queued,
                Sequence = ++nextSequence
            };
            registry.Add(entry.TaskId, entry);
            queue.Enqueue(entry.TaskId);
        }

        TryDispatch();
        return ValueTask.FromResult(GetSnapshot(entry.TaskId)!);
    }

    public ValueTask<BackgroundTaskSnapshot?> GetAsync(
        BackgroundTaskId taskId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(GetSnapshot(taskId));
    }

    public ValueTask<IReadOnlyList<BackgroundTaskSnapshot>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            IReadOnlyList<BackgroundTaskSnapshot> snapshots = registry.Values
                .OrderByDescending(entry => entry.Sequence)
                .Select(SnapshotLocked)
                .ToArray();
            return ValueTask.FromResult(snapshots);
        }
    }

    public ValueTask<BackgroundTaskSnapshot?> CancelAsync(
        BackgroundTaskId taskId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Entry? signal = null;
        CancellationTokenSource? dispose = null;
        BackgroundTaskSnapshot? snapshot;
        lock (sync)
        {
            if (!registry.TryGetValue(taskId, out var entry))
            {
                return ValueTask.FromResult<BackgroundTaskSnapshot?>(null);
            }

            if (entry.State == BackgroundTaskState.Queued)
            {
                entry.State = BackgroundTaskState.Cancelled;
                entry.FinishedAt = DateTimeOffset.UtcNow;
                entry.Terminal.TrySetResult();
                AddTerminalLocked(entry);
                dispose = entry.Cancellation;
            }
            else if (entry.State == BackgroundTaskState.Running)
            {
                entry.State = BackgroundTaskState.Cancelling;
                signal = entry;
            }

            snapshot = SnapshotLocked(entry);
        }

        dispose?.Dispose();
        if (signal is not null)
        {
            _ = Task.Run(signal.Cancellation.Cancel, CancellationToken.None);
        }

        TryDispatch();
        return ValueTask.FromResult<BackgroundTaskSnapshot?>(snapshot);
    }

    public async ValueTask<bool> WaitAsync(
        BackgroundTaskId taskId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        Task? terminal;
        lock (sync)
        {
            terminal = registry.TryGetValue(taskId, out var entry) ? entry.Terminal.Task : null;
        }

        if (terminal is null)
        {
            return false;
        }

        try
        {
            await terminal.WaitAsync(timeout, cancellationToken);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public bool TryGetResult<TResult>(BackgroundTaskId taskId, out TResult? result)
    {
        lock (sync)
        {
            if (registry.TryGetValue(taskId, out var entry) &&
                entry.State == BackgroundTaskState.Completed &&
                entry.Result is TResult typed)
            {
                result = typed;
                return true;
            }
        }

        result = default;
        return false;
    }

    public void Dispose()
    {
        Entry[] entries;
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            entries = registry.Values.Where(entry => !IsTerminal(entry.State)).ToArray();
            foreach (var entry in entries)
            {
                if (entry.State == BackgroundTaskState.Queued)
                {
                    entry.State = BackgroundTaskState.Cancelled;
                    entry.FinishedAt = DateTimeOffset.UtcNow;
                    entry.Terminal.TrySetResult();
                    AddTerminalLocked(entry);
                }
                else
                {
                    entry.State = BackgroundTaskState.Cancelling;
                }
            }
        }

        foreach (var entry in entries)
        {
            try
            {
                entry.Cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The worker completed while disposal was in progress.
            }
        }
    }

    private void TryDispatch()
    {
        Entry? entry = null;
        lock (sync)
        {
            if (disposed || activeTaskId is not null)
            {
                return;
            }

            while (queue.TryDequeue(out var taskId))
            {
                if (!registry.TryGetValue(taskId, out var candidate) || candidate.State != BackgroundTaskState.Queued)
                {
                    continue;
                }

                entry = candidate;
                entry.State = BackgroundTaskState.Running;
                entry.StartedAt = DateTimeOffset.UtcNow;
                activeTaskId = taskId;
                break;
            }
        }

        if (entry is not null)
        {
            entry.Execution = Task.Run(() => ExecuteAsync(entry), CancellationToken.None);
        }
    }

    private async Task ExecuteAsync(Entry entry)
    {
        var terminalState = BackgroundTaskState.Completed;
        string? errorCode = null;
        string? errorMessage = null;
        object? result = null;
        try
        {
            var worker = serviceProvider.GetRequiredKeyedService<IBackgroundTaskWorker>(entry.Kind);
            if (worker.Kind != entry.Kind || !worker.RequestType.IsInstanceOfType(entry.Request))
            {
                throw new BackgroundTaskFailureException("background_task_worker_mismatch", "Background task worker configuration is invalid.");
            }

            result = await worker.ExecuteAsync(
                entry.Request,
                new Context(entry.TaskId, Report),
                entry.Cancellation.Token);
        }
        catch (OperationCanceledException) when (entry.Cancellation.IsCancellationRequested)
        {
            terminalState = BackgroundTaskState.Cancelled;
        }
        catch (BackgroundTaskFailureException exception)
        {
            terminalState = BackgroundTaskState.Failed;
            errorCode = exception.Code;
            errorMessage = exception.Message;
        }
        catch (Exception)
        {
            terminalState = BackgroundTaskState.Failed;
            errorCode = "background_task_failed";
            errorMessage = "Background task failed.";
        }
        finally
        {
            lock (sync)
            {
                if (entry.Cancellation.IsCancellationRequested)
                {
                    terminalState = BackgroundTaskState.Cancelled;
                    result = null;
                    errorCode = null;
                    errorMessage = null;
                }

                entry.State = terminalState;
                entry.Result = result;
                entry.ErrorCode = errorCode;
                entry.ErrorMessage = errorMessage;
                entry.FinishedAt = DateTimeOffset.UtcNow;
                activeTaskId = null;
                AddTerminalLocked(entry);
            }

            entry.Cancellation.Dispose();
            entry.Terminal.TrySetResult();
            TryDispatch();
        }
    }

    private void Report(
        BackgroundTaskId taskId,
        string step,
        int? completed,
        int? total,
        string? detail,
        string? subject)
    {
        lock (sync)
        {
            if (!registry.TryGetValue(taskId, out var entry) || IsTerminal(entry.State))
            {
                return;
            }

            entry.Step = step;
            entry.Completed = completed;
            entry.Total = total;
            entry.Detail = detail;
            entry.Subject = subject ?? entry.Subject;
        }
    }

    private BackgroundTaskSnapshot? GetSnapshot(BackgroundTaskId taskId)
    {
        lock (sync)
        {
            return registry.TryGetValue(taskId, out var entry) ? SnapshotLocked(entry) : null;
        }
    }

    private void AddTerminalLocked(Entry entry)
    {
        terminalOrder.Enqueue(entry.TaskId);
        while (terminalOrder.Count > MaximumTerminalHistory)
        {
            registry.Remove(terminalOrder.Dequeue());
        }
    }

    private static BackgroundTaskSnapshot SnapshotLocked(Entry entry) => new(
        entry.TaskId,
        entry.Kind,
        entry.State,
        entry.Key,
        entry.Subject,
        entry.Step,
        entry.Completed,
        entry.Total,
        entry.Detail,
        entry.StartedAt,
        entry.FinishedAt,
        entry.ErrorCode,
        entry.ErrorMessage);

    private static bool IsTerminal(BackgroundTaskState state) =>
        state is BackgroundTaskState.Completed or BackgroundTaskState.Failed or BackgroundTaskState.Cancelled;

    private sealed class Entry
    {
        public required BackgroundTaskId TaskId { get; init; }
        public required BackgroundTaskKind Kind { get; init; }
        public required string Key { get; init; }
        public string? Subject { get; set; }
        public required object Request { get; init; }
        public required BackgroundTaskState State { get; set; }
        public required long Sequence { get; init; }
        public CancellationTokenSource Cancellation { get; } = new();
        public TaskCompletionSource Terminal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task? Execution { get; set; }
        public object? Result { get; set; }
        public string? Step { get; set; }
        public int? Completed { get; set; }
        public int? Total { get; set; }
        public string? Detail { get; set; }
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? FinishedAt { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
    }

    private sealed class Context(BackgroundTaskId taskId, Action<BackgroundTaskId, string, int?, int?, string?, string?> report) : IBackgroundTaskContext
    {
        public BackgroundTaskId TaskId { get; } = taskId;

        public void Report(string step, int? completed = null, int? total = null, string? detail = null, string? subject = null) =>
            report(TaskId, step, completed, total, detail, subject);
    }
}
