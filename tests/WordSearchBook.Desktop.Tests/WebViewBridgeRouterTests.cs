using System.IO;
using System.Text.Json;
using WordSearchBook.Core.Application;
using WordSearchBook.Core.Application.BackgroundTasks;
using WordSearchBook.Core.Application.Workspace;
using WordSearchBook.Desktop.Bridge;

namespace WordSearchBook.Desktop.Tests;

public sealed class WebViewBridgeRouterTests
{
    [Fact]
    public async Task ReturnsTypedPongForPing()
    {
        var router = CreateRouter(out _);
        using var response = JsonDocument.Parse(await router.HandleAsync("""{"id":"request-1","type":"ping"}"""));
        var root = response.RootElement;

        Assert.Equal("request-1", root.GetProperty("id").GetString());
        Assert.Equal("pong", root.GetProperty("type").GetString());
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal("Word Search Book", root.GetProperty("data").GetProperty("name").GetString());
        Assert.Equal("0.1.0", root.GetProperty("data").GetProperty("version").GetString());
        Assert.Equal("ready", root.GetProperty("data").GetProperty("status").GetString());
        Assert.False(root.TryGetProperty("error", out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-json")]
    [InlineData("{}")]
    public async Task ReturnsStableErrorForMalformedMessages(string? message)
    {
        using var response = JsonDocument.Parse(await CreateRouter(out _).HandleAsync(message));
        var root = response.RootElement;

        Assert.Equal("error", root.GetProperty("type").GetString());
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("malformed_message", root.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task ReturnsStableErrorForUnsupportedMessages()
    {
        using var response = JsonDocument.Parse(await CreateRouter(out _).HandleAsync("""{"id":"request-2","type":"unknown"}"""));
        var root = response.RootElement;

        Assert.Equal("request-2", root.GetProperty("id").GetString());
        Assert.Equal("unsupported_message", root.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task EnqueuesWorkspaceRefreshAgainstExecutableRoot()
    {
        var router = CreateRouter(out var manager);

        using var response = JsonDocument.Parse(await router.HandleAsync("""{"id":"refresh-1","type":"workspace.refresh"}"""));

        Assert.True(response.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("background.task", response.RootElement.GetProperty("type").GetString());
        Assert.Equal(BackgroundTaskKind.WorkspaceRefresh, manager.LastKind);
        var request = Assert.IsType<WorkspaceRefreshRequest>(manager.LastRequest);
        Assert.Equal(Path.GetFullPath("application-root"), request.RootPath);
    }

    [Fact]
    public async Task EnqueuesBookGenerationWithTypedPayload()
    {
        var router = CreateRouter(out var manager);

        using var response = JsonDocument.Parse(await router.HandleAsync(
            """{"id":"generate-1","type":"book.generate","payload":{"bookId":"book-one","brandId":"demo"}}"""));

        Assert.True(response.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(BackgroundTaskKind.BookGeneration, manager.LastKind);
        var request = Assert.IsType<BookGenerationTaskRequest>(manager.LastRequest);
        Assert.Equal(("book-one", "demo"), (request.BookId, request.BrandId));
    }

    [Fact]
    public async Task RejectsInvalidCommandPayload()
    {
        using var response = JsonDocument.Parse(await CreateRouter(out _).HandleAsync(
            """{"id":"generate-2","type":"book.generate","payload":{"bookId":"book-one"}}"""));

        Assert.Equal("invalid_payload", response.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task EnqueuesTypedGlobalSettingsSave()
    {
        var router = CreateRouter(out var manager);

        using var response = JsonDocument.Parse(await router.HandleAsync(
            """{"id":"settings-1","type":"settings.global.save","payload":{"settings":{"board":{"width":20,"height":20},"page":{"width":2400,"height":3000}}}}"""));

        Assert.True(response.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(BackgroundTaskKind.SettingsSave, manager.LastKind);
        var request = Assert.IsType<GlobalSettingsSaveTaskRequest>(manager.LastRequest);
        Assert.Equal(2400, request.Settings.Page.Width);
    }

    [Fact]
    public void ResolvesFrontendEntryPointBelowBaseDirectory()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "word-search-book-tests");
        var result = FrontendPathResolver.GetIndexPath(baseDirectory);
        Assert.Equal(Path.GetFullPath(Path.Combine(baseDirectory, "Frontend", "index.html")), result);
    }

    private static WebViewBridgeRouter CreateRouter(out StubTaskManager manager)
    {
        manager = new StubTaskManager();
        return new WebViewBridgeRouter(
            new StubApplicationInfoProvider(),
            manager,
            new StubRootProvider(Path.GetFullPath("application-root")));
    }

    private sealed class StubApplicationInfoProvider : IApplicationInfoProvider
    {
        public ApplicationInfo GetCurrent() => new("Word Search Book", "0.1.0", "ready");
    }

    private sealed record StubRootProvider(string RootPath) : IApplicationRootProvider;

    private sealed class StubTaskManager : IBackgroundTaskManager
    {
        private readonly Dictionary<BackgroundTaskId, BackgroundTaskSnapshot> tasks = [];

        public BackgroundTaskKind? LastKind { get; private set; }

        public object? LastRequest { get; private set; }

        public ValueTask<BackgroundTaskSnapshot> StartAsync<TRequest>(
            BackgroundTaskKind kind,
            string key,
            string? subject,
            TRequest request,
            CancellationToken cancellationToken = default)
        {
            LastKind = kind;
            LastRequest = request;
            var task = new BackgroundTaskSnapshot(
                BackgroundTaskId.New(), kind, BackgroundTaskState.Queued, key, subject,
                null, null, null, null, null, null, null, null);
            tasks.Add(task.TaskId, task);
            return ValueTask.FromResult(task);
        }

        public ValueTask<BackgroundTaskSnapshot?> GetAsync(BackgroundTaskId taskId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(tasks.GetValueOrDefault(taskId));

        public ValueTask<IReadOnlyList<BackgroundTaskSnapshot>> ListAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<BackgroundTaskSnapshot>>(tasks.Values.ToArray());

        public ValueTask<BackgroundTaskSnapshot?> CancelAsync(BackgroundTaskId taskId, CancellationToken cancellationToken = default) =>
            GetAsync(taskId, cancellationToken);

        public ValueTask<bool> WaitAsync(BackgroundTaskId taskId, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(tasks.ContainsKey(taskId));

        public bool TryGetResult<TResult>(BackgroundTaskId taskId, out TResult? result)
        {
            result = default;
            return false;
        }
    }
}
