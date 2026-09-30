using WordSearchBook.Core.Application.BackgroundTasks;

namespace WordSearchBook.Desktop.Shutdown;

public sealed class ApplicationCloseCoordinator(IBackgroundTaskManager taskManager)
{
    internal static bool IsActive(BackgroundTaskState state) =>
        state is BackgroundTaskState.Queued or BackgroundTaskState.Running or BackgroundTaskState.Cancelling;

    public async ValueTask<IReadOnlyList<BackgroundTaskSnapshot>> GetActiveTasksAsync(
        CancellationToken cancellationToken = default)
    {
        var tasks = await taskManager.ListAsync(cancellationToken);
        return tasks.Where(task => IsActive(task.State)).ToArray();
    }

    public async Task CancelAndWaitAsync(
        IReadOnlyCollection<BackgroundTaskSnapshot> tasks,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "The close timeout must be positive.");
        }

        await Task.WhenAll(tasks.Select(task =>
            taskManager.CancelAsync(task.TaskId, cancellationToken).AsTask()));
        await Task.WhenAll(tasks.Select(task =>
            taskManager.WaitAsync(task.TaskId, timeout, cancellationToken).AsTask()));
    }
}
