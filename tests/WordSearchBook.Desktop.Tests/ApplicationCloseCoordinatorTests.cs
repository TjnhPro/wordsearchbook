using WordSearchBook.Core.Application.BackgroundTasks;
using WordSearchBook.Desktop.Shutdown;

namespace WordSearchBook.Desktop.Tests;

public sealed class ApplicationCloseCoordinatorTests
{
    [Fact]
    public async Task ReturnsOnlyTasksThatCanDelayClosing()
    {
        var manager = new RecordingTaskManager(
        [
            CreateTask(BackgroundTaskState.Queued),
            CreateTask(BackgroundTaskState.Running),
            CreateTask(BackgroundTaskState.Cancelling),
            CreateTask(BackgroundTaskState.Completed),
            CreateTask(BackgroundTaskState.Failed),
            CreateTask(BackgroundTaskState.Cancelled)
        ]);

        var active = await new ApplicationCloseCoordinator(manager).GetActiveTasksAsync();

        Assert.Equal(3, active.Count);
        Assert.All(active, task => Assert.True(ApplicationCloseCoordinator.IsActive(task.State)));
    }

    [Fact]
    public async Task CancelsEveryTaskThenWaitsForAllWithOneSharedTimeout()
    {
        var tasks = new[]
        {
            CreateTask(BackgroundTaskState.Running),
            CreateTask(BackgroundTaskState.Queued)
        };
        var manager = new RecordingTaskManager(tasks);
        var coordinator = new ApplicationCloseCoordinator(manager);
        var timeout = TimeSpan.FromSeconds(5);

        await coordinator.CancelAndWaitAsync(tasks, timeout);

        Assert.Equal(tasks.Select(task => task.TaskId), manager.CancelledTaskIds);
        Assert.Equal(tasks.Select(task => task.TaskId), manager.WaitedTaskIds);
        Assert.All(manager.WaitTimeouts, value => Assert.Equal(timeout, value));
        Assert.True(manager.AllTasksWereCancelledBeforeFirstWait);
    }

    [Fact]
    public async Task EmptyTaskSetCompletesWithoutManagerCalls()
    {
        var manager = new RecordingTaskManager([]);

        await new ApplicationCloseCoordinator(manager).CancelAndWaitAsync([], TimeSpan.FromSeconds(5));

        Assert.Empty(manager.CancelledTaskIds);
        Assert.Empty(manager.WaitedTaskIds);
    }

    [Fact]
    public async Task ContinuesClosingWhenTheSharedWaitTimesOut()
    {
        var task = CreateTask(BackgroundTaskState.Running);
        var manager = new RecordingTaskManager([task]) { WaitResult = false };
        var timeout = TimeSpan.FromMilliseconds(25);

        await new ApplicationCloseCoordinator(manager).CancelAndWaitAsync([task], timeout);

        Assert.Equal([task.TaskId], manager.CancelledTaskIds);
        Assert.Equal([task.TaskId], manager.WaitedTaskIds);
        Assert.Equal([timeout], manager.WaitTimeouts);
    }

    private static BackgroundTaskSnapshot CreateTask(BackgroundTaskState state) => new(
        BackgroundTaskId.New(),
        BackgroundTaskKind.WorkspaceRefresh,
        state,
        "workspace",
        "Workspace",
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null);

    private sealed class RecordingTaskManager(IReadOnlyList<BackgroundTaskSnapshot> tasks)
        : IBackgroundTaskManager
    {
        public List<BackgroundTaskId> CancelledTaskIds { get; } = [];

        public List<BackgroundTaskId> WaitedTaskIds { get; } = [];

        public List<TimeSpan> WaitTimeouts { get; } = [];

        public bool AllTasksWereCancelledBeforeFirstWait { get; private set; }

        public bool WaitResult { get; init; } = true;

        public ValueTask<BackgroundTaskSnapshot> StartAsync<TRequest>(
            BackgroundTaskKind kind,
            string key,
            string? subject,
            TRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<BackgroundTaskSnapshot?> GetAsync(
            BackgroundTaskId taskId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<BackgroundTaskSnapshot?>(null);

        public ValueTask<IReadOnlyList<BackgroundTaskSnapshot>> ListAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(tasks);

        public ValueTask<BackgroundTaskSnapshot?> CancelAsync(
            BackgroundTaskId taskId,
            CancellationToken cancellationToken = default)
        {
            CancelledTaskIds.Add(taskId);
            return ValueTask.FromResult<BackgroundTaskSnapshot?>(null);
        }

        public ValueTask<bool> WaitAsync(
            BackgroundTaskId taskId,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            if (WaitedTaskIds.Count == 0)
            {
                AllTasksWereCancelledBeforeFirstWait = CancelledTaskIds.Count == tasks.Count;
            }

            WaitedTaskIds.Add(taskId);
            WaitTimeouts.Add(timeout);
            return ValueTask.FromResult(WaitResult);
        }

        public bool TryGetResult<TResult>(BackgroundTaskId taskId, out TResult? result)
        {
            result = default;
            return false;
        }
    }
}
