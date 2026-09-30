namespace WordSearchBook.Core.Application.BackgroundTasks;

public interface IBackgroundTaskManager
{
    ValueTask<BackgroundTaskSnapshot> StartAsync<TRequest>(
        BackgroundTaskKind kind,
        string key,
        string? subject,
        TRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<BackgroundTaskSnapshot?> GetAsync(BackgroundTaskId taskId, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<BackgroundTaskSnapshot>> ListAsync(CancellationToken cancellationToken = default);

    ValueTask<BackgroundTaskSnapshot?> CancelAsync(BackgroundTaskId taskId, CancellationToken cancellationToken = default);

    ValueTask<bool> WaitAsync(BackgroundTaskId taskId, TimeSpan timeout, CancellationToken cancellationToken = default);

    bool TryGetResult<TResult>(BackgroundTaskId taskId, out TResult? result);
}

public interface IBackgroundTaskContext
{
    BackgroundTaskId TaskId { get; }

    void Report(
        string step,
        int? completed = null,
        int? total = null,
        string? detail = null,
        string? subject = null);
}

public interface IBackgroundTaskWorker
{
    BackgroundTaskKind Kind { get; }

    Type RequestType { get; }

    ValueTask<object?> ExecuteAsync(
        object request,
        IBackgroundTaskContext context,
        CancellationToken cancellationToken);
}

public abstract class BackgroundTaskWorker<TRequest, TResult> : IBackgroundTaskWorker
{
    public abstract BackgroundTaskKind Kind { get; }

    public Type RequestType => typeof(TRequest);

    protected abstract ValueTask<TResult> ExecuteTypedAsync(
        TRequest request,
        IBackgroundTaskContext context,
        CancellationToken cancellationToken);

    async ValueTask<object?> IBackgroundTaskWorker.ExecuteAsync(
        object request,
        IBackgroundTaskContext context,
        CancellationToken cancellationToken)
    {
        if (request is not TRequest typed)
        {
            throw new ArgumentException($"Worker {Kind} requires request type {typeof(TRequest).Name}.", nameof(request));
        }

        return await ExecuteTypedAsync(typed, context, cancellationToken);
    }
}
