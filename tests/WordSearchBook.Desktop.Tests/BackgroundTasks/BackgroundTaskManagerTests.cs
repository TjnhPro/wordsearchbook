using Microsoft.Extensions.DependencyInjection;
using WordSearchBook.Core.Application.BackgroundTasks;
using WordSearchBook.Desktop.BackgroundTasks;

namespace WordSearchBook.Desktop.Tests.BackgroundTasks;

public sealed class BackgroundTaskManagerTests
{
    [Fact]
    public async Task RunsDistinctTasksInOneFifoLaneAndJoinsExactDuplicates()
    {
        var tracker = new ConcurrencyTracker();
        var refresh = new BlockingWorker(BackgroundTaskKind.WorkspaceRefresh, tracker);
        var generation = new BlockingWorker(BackgroundTaskKind.BookGeneration, tracker);
        using var host = new TestHost(refresh, generation);

        var first = await host.Manager.StartAsync(refresh.Kind, "workspace", "Workspace", new Request("refresh"));
        await refresh.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var duplicate = await host.Manager.StartAsync(refresh.Kind, "workspace", "Workspace", new Request("refresh"));
        var second = await host.Manager.StartAsync(generation.Kind, "book:brand", "Book", new Request("generate"));

        Assert.Equal(first.TaskId, duplicate.TaskId);
        Assert.Equal(BackgroundTaskState.Queued, (await host.Manager.GetAsync(second.TaskId))!.State);
        Assert.False(generation.Started.Task.IsCompleted);

        refresh.Release.TrySetResult();
        await generation.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        generation.Release.TrySetResult();

        Assert.True(await host.Manager.WaitAsync(first.TaskId, TimeSpan.FromSeconds(2)));
        Assert.True(await host.Manager.WaitAsync(second.TaskId, TimeSpan.FromSeconds(2)));
        Assert.Equal(1, tracker.MaximumActive);
    }

    [Fact]
    public async Task CancelsRunningWorkAndRetainsCancelledState()
    {
        var worker = new CancellableWorker();
        using var host = new TestHost(worker);
        var task = await host.Manager.StartAsync(worker.Kind, "generation", "Book", new Request("generate"));
        await worker.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var cancelling = await host.Manager.CancelAsync(task.TaskId);

        Assert.Equal(BackgroundTaskState.Cancelling, cancelling!.State);
        Assert.True(await host.Manager.WaitAsync(task.TaskId, TimeSpan.FromSeconds(2)));
        Assert.Equal(BackgroundTaskState.Cancelled, (await host.Manager.GetAsync(task.TaskId))!.State);
    }

    [Fact]
    public async Task SanitizesUnexpectedWorkerFailures()
    {
        using var host = new TestHost(new ThrowingWorker());
        var task = await host.Manager.StartAsync(BackgroundTaskKind.SettingsSave, "settings", null, new Request("save"));

        Assert.True(await host.Manager.WaitAsync(task.TaskId, TimeSpan.FromSeconds(2)));
        var failed = await host.Manager.GetAsync(task.TaskId);

        Assert.Equal(BackgroundTaskState.Failed, failed!.State);
        Assert.Equal("background_task_failed", failed.ErrorCode);
        Assert.Equal("Background task failed.", failed.ErrorMessage);
        Assert.DoesNotContain("secret", failed.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecutesWorkerOutsideTheCallersSynchronizationContext()
    {
        var worker = new ContextWorker();
        using var host = new TestHost(worker);
        var previous = SynchronizationContext.Current;
        var marker = new SynchronizationContext();
        try
        {
            SynchronizationContext.SetSynchronizationContext(marker);
            var task = await host.Manager.StartAsync(worker.Kind, "workspace", null, new Request("refresh"));
            Assert.True(await host.Manager.WaitAsync(task.TaskId, TimeSpan.FromSeconds(2)));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        Assert.Null(worker.ObservedContext);
    }

    private sealed record Request(string Value);

    private sealed class TestHost : IDisposable
    {
        private readonly ServiceProvider services;

        public TestHost(params IBackgroundTaskWorker[] workers)
        {
            var collection = new ServiceCollection();
            foreach (var worker in workers)
            {
                collection.AddKeyedSingleton<IBackgroundTaskWorker>(worker.Kind, worker);
            }

            services = collection.BuildServiceProvider();
            Manager = new BackgroundTaskManager(services);
        }

        public BackgroundTaskManager Manager { get; }

        public void Dispose()
        {
            Manager.Dispose();
            services.Dispose();
        }
    }

    private sealed class ConcurrencyTracker
    {
        private int active;

        public int MaximumActive { get; private set; }

        public void Enter()
        {
            var current = Interlocked.Increment(ref active);
            MaximumActive = Math.Max(MaximumActive, current);
        }

        public void Exit() => Interlocked.Decrement(ref active);
    }

    private sealed class BlockingWorker(BackgroundTaskKind kind, ConcurrencyTracker tracker) : BackgroundTaskWorker<Request, string>
    {
        public override BackgroundTaskKind Kind { get; } = kind;

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async ValueTask<string> ExecuteTypedAsync(
            Request request,
            IBackgroundTaskContext context,
            CancellationToken cancellationToken)
        {
            tracker.Enter();
            try
            {
                context.Report("running", detail: request.Value);
                Started.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
                return request.Value;
            }
            finally
            {
                tracker.Exit();
            }
        }
    }

    private sealed class CancellableWorker : BackgroundTaskWorker<Request, string>
    {
        public override BackgroundTaskKind Kind => BackgroundTaskKind.BookGeneration;

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async ValueTask<string> ExecuteTypedAsync(
            Request request,
            IBackgroundTaskContext context,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return request.Value;
        }
    }

    private sealed class ThrowingWorker : BackgroundTaskWorker<Request, string>
    {
        public override BackgroundTaskKind Kind => BackgroundTaskKind.SettingsSave;

        protected override ValueTask<string> ExecuteTypedAsync(
            Request request,
            IBackgroundTaskContext context,
            CancellationToken cancellationToken) => throw new InvalidOperationException("D:\\secret");
    }

    private sealed class ContextWorker : BackgroundTaskWorker<Request, string>
    {
        public override BackgroundTaskKind Kind => BackgroundTaskKind.WorkspaceRefresh;

        public SynchronizationContext? ObservedContext { get; private set; }

        protected override ValueTask<string> ExecuteTypedAsync(
            Request request,
            IBackgroundTaskContext context,
            CancellationToken cancellationToken)
        {
            ObservedContext = SynchronizationContext.Current;
            return ValueTask.FromResult(request.Value);
        }
    }
}
