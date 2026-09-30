using System.IO;
using System.Text.Json;
using WordSearchBook.Core.Application;
using WordSearchBook.Core.Application.BackgroundTasks;
using WordSearchBook.Core.Application.Workspace;
using WordSearchBook.Core.WordSearch.Validation;
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
            """{"id":"settings-1","type":"settings.global.save","payload":{"settings":{"board":{"width":20,"height":20},"page":{"width":2588,"height":3375}}}}"""));

        Assert.True(response.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(BackgroundTaskKind.SettingsSave, manager.LastKind);
        var request = Assert.IsType<GlobalSettingsSaveTaskRequest>(manager.LastRequest);
        Assert.Equal(2588, request.Settings.Page.Width);
    }

    [Fact]
    public async Task EnqueuesTypedBrandCreate()
    {
        var router = CreateRouter(out var manager);

        using var response = JsonDocument.Parse(await router.HandleAsync(
            """{"id":"brand-create-1","type":"brand.create","payload":{"brandId":"new-brand"}}"""));

        Assert.True(response.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(BackgroundTaskKind.BrandCreate, manager.LastKind);
        var request = Assert.IsType<BrandCreateTaskRequest>(manager.LastRequest);
        Assert.Equal("new-brand", request.BrandId);
        Assert.Equal(Path.GetFullPath("application-root"), request.RootPath);
    }

    [Fact]
    public async Task EnqueuesTypedBrandLayoutValidation()
    {
        var router = CreateRouter(out var manager);

        using var response = JsonDocument.Parse(await router.HandleAsync(
            """{"id":"brand-validation-1","type":"brand.layout.validate","payload":{"brandId":"demo"}}"""));

        Assert.True(response.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(BackgroundTaskKind.BrandPageLayoutValidation, manager.LastKind);
        var request = Assert.IsType<BrandPageLayoutValidationRequest>(manager.LastRequest);
        Assert.Equal("demo", request.BrandId);
        Assert.Equal(Path.GetFullPath("application-root"), request.RootPath);
    }

    [Theory]
    [InlineData("../demo")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    public async Task RejectsUnsafeBrandLayoutValidationPayload(string brandId)
    {
        var router = CreateRouter(out var manager);
        var message = JsonSerializer.Serialize(new
        {
            id = "brand-validation-invalid",
            type = "brand.layout.validate",
            payload = new { brandId }
        });

        using var response = JsonDocument.Parse(await router.HandleAsync(message));

        Assert.False(response.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_payload", response.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Null(manager.LastKind);
    }

    [Fact]
    public async Task TaskDetailMapsValidationSnapshotAndFailuresWithoutChangingResultShape()
    {
        var router = CreateRouter(out var manager);
        var snapshot = new WorkspaceSnapshot(
            Path.GetFullPath("application-root"),
            null,
            null,
            [],
            [],
            DateTimeOffset.UtcNow);
        var validation = new BrandValidationResult(
            new BrandValidationState(BrandValidationStatus.NotValidated),
            [new BrandValidationFailure("page_layout.png", "exists", "page_layout_not_found", "Missing")]);
        var taskId = manager.AddCompleted(new BrandPageLayoutValidationTaskResult(snapshot, validation));
        var message = JsonSerializer.Serialize(new
        {
            id = "task-detail-1",
            type = "task.get",
            payload = new { taskId = taskId.Value }
        });

        using var response = JsonDocument.Parse(await router.HandleAsync(message));

        var data = response.RootElement.GetProperty("data");
        Assert.Equal(snapshot.RootPath, data.GetProperty("result").GetProperty("rootPath").GetString());
        Assert.Equal(
            "page_layout_not_found",
            data.GetProperty("brandValidationResult").GetProperty("failures")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task EnqueuesTypedBrandSettingsSave()
    {
        var router = CreateRouter(out var manager);
        const string boardRegion = """{"rectangle":{"x":10,"y":20,"width":2000,"height":2000},"font":{"name":"Arial","size":24,"color":"#112233"}}""";
        const string textRegion = """{"x":10,"y":20,"font":{"name":"Arial","size":24,"color":"#112233"},"alignment":"Center"}""";
        const string keywordRegion = """{"columns":[{"x":100,"y":2100},{"x":600,"y":2100},{"x":1100,"y":2100},{"x":1600,"y":2100}],"stepY":60,"font":{"name":"Arial","size":24,"color":"#112233"},"alignment":"Center"}""";
        var message = $$"""
            {
              "id": "brand-settings-1",
              "type": "settings.brand.save",
              "payload": {
                "brandId": "demo",
                "settings": {
                  "topic": {{textRegion}},
                  "boardGame": {{boardRegion}},
                  "keywordList": {{keywordRegion}},
                  "pageNumber": {{textRegion}},
                  "answerLine": { "width": 2.5, "color": "#AABBCC" }
                }
              }
            }
            """;

        using var response = JsonDocument.Parse(await router.HandleAsync(message));

        Assert.True(response.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(BackgroundTaskKind.SettingsSave, manager.LastKind);
        var request = Assert.IsType<BrandSettingsSaveTaskRequest>(manager.LastRequest);
        Assert.Equal("demo", request.BrandId);
        Assert.Equal(10, request.Settings.Topic.X);
        Assert.Equal("Arial", request.Settings.BoardGame.Font.Name);
        Assert.Equal(2.5f, request.Settings.AnswerLine.Width);
        Assert.Equal("#AABBCC", request.Settings.AnswerLine.Color);
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
        private readonly Dictionary<BackgroundTaskId, object> results = [];

        public BackgroundTaskKind? LastKind { get; private set; }

        public object? LastRequest { get; private set; }

        public BackgroundTaskId AddCompleted(object result)
        {
            var taskId = BackgroundTaskId.New();
            tasks.Add(taskId, new BackgroundTaskSnapshot(
                taskId,
                BackgroundTaskKind.BrandPageLayoutValidation,
                BackgroundTaskState.Completed,
                "brand-layout:demo",
                "demo",
                "Refreshing workspace",
                null,
                null,
                null,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                null,
                null));
            results.Add(taskId, result);
            return taskId;
        }

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
            if (results.TryGetValue(taskId, out var value) && value is TResult typed)
            {
                result = typed;
                return true;
            }

            result = default;
            return false;
        }
    }
}
