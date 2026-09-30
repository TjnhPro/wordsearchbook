using System.IO;
using System.Text.Json;
using WordSearchBook.Core.Application;
using WordSearchBook.Core.Application.BackgroundTasks;
using WordSearchBook.Core.Application.Workspace;
using WordSearchBook.Core.WordSearch.Application;
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
    public async Task EnqueuesBookProcessingWithTypedPayload()
    {
        var router = CreateRouter(out var manager);

        using var response = JsonDocument.Parse(await router.HandleAsync(
            """{"id":"process-1","type":"book.process","payload":{"bookId":"book-one","brandId":"demo"}}"""));

        Assert.True(response.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(BackgroundTaskKind.BookProcessing, manager.LastKind);
        var request = Assert.IsType<BookProcessingTaskRequest>(manager.LastRequest);
        Assert.Equal(("book-one", "demo"), (request.BookId, request.BrandId));
    }

    [Fact]
    public async Task EnqueuesBookDataValidationWithTypedPayload()
    {
        var router = CreateRouter(out var manager);

        using var response = JsonDocument.Parse(await router.HandleAsync(
            """{"id":"data-1","type":"book.data.validate","payload":{"bookId":"book-one"}}"""));

        Assert.True(response.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(BackgroundTaskKind.BookDataValidation, manager.LastKind);
        var request = Assert.IsType<BookDataValidationRequest>(manager.LastRequest);
        Assert.Equal("book-one", request.BookId);
    }

    [Fact]
    public async Task RejectsInvalidCommandPayload()
    {
        using var response = JsonDocument.Parse(await CreateRouter(out _).HandleAsync(
            """{"id":"process-2","type":"book.process","payload":{"bookId":"book-one"}}"""));

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
    public async Task EnqueuesTypedBrandValidation()
    {
        var router = CreateRouter(out var manager);

        using var response = JsonDocument.Parse(await router.HandleAsync(
            """{"id":"brand-validation-1","type":"brand.validate","payload":{"brandId":"demo"}}"""));

        Assert.True(response.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(BackgroundTaskKind.BrandValidation, manager.LastKind);
        var request = Assert.IsType<BrandValidationRequest>(manager.LastRequest);
        Assert.Equal("demo", request.BrandId);
        Assert.Equal(Path.GetFullPath("application-root"), request.RootPath);
    }

    [Fact]
    public async Task EnqueuesTypedBrandPagePreview()
    {
        var router = CreateRouter(out var manager);

        using var response = JsonDocument.Parse(await router.HandleAsync(
            """{"id":"brand-preview-1","type":"brand.preview.draw","payload":{"brandId":"demo"}}"""));

        Assert.True(response.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(BackgroundTaskKind.BrandPagePreview, manager.LastKind);
        var request = Assert.IsType<BrandPagePreviewTaskRequest>(manager.LastRequest);
        Assert.Equal("demo", request.BrandId);
        Assert.Equal(Path.GetFullPath("application-root"), request.RootPath);
    }

    [Fact]
    public async Task OpensBrandFolderUsingDesktopRoot()
    {
        var router = CreateRouter(out _, out var folderAction);

        using var response = JsonDocument.Parse(await router.HandleAsync(
            """{"id":"brand-folder-1","type":"brand.folder.open","payload":{"brandId":"demo"}}"""));

        Assert.True(response.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("brand.folder.opened", response.RootElement.GetProperty("type").GetString());
        Assert.Equal(Path.GetFullPath("application-root"), folderAction.RootPath);
        Assert.Equal("demo", folderAction.BrandId);
    }

    [Fact]
    public async Task OpensBookOutputFolderUsingDesktopRoot()
    {
        var router = CreateRouter(out _, out _, out var outputFolderAction);

        using var response = JsonDocument.Parse(await router.HandleAsync(
            """{"id":"book-output-1","type":"book.output.open","payload":{"bookId":"book-one"}}"""));

        Assert.True(response.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("book.output.opened", response.RootElement.GetProperty("type").GetString());
        Assert.Equal(Path.GetFullPath("application-root"), outputFolderAction.RootPath);
        Assert.Equal("book-one", outputFolderAction.BookId);
    }

    [Theory]
    [InlineData("../demo")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    public async Task RejectsUnsafeBrandValidationPayload(string brandId)
    {
        var router = CreateRouter(out var manager);
        var message = JsonSerializer.Serialize(new
        {
            id = "brand-validation-invalid",
            type = "brand.validate",
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
        var taskId = manager.AddCompleted(new BrandValidationTaskResult(snapshot, validation));
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
    public async Task TaskDetailIncludesCompletedBrandPagePreviewResult()
    {
        var router = CreateRouter(out var manager);
        var preview = new BrandPagePreviewResult(
            "demo",
            BrandPagePreviewSample.OutputFileName,
            2588,
            3375,
            DateTimeOffset.UtcNow);
        var taskId = manager.AddCompleted(preview, BackgroundTaskKind.BrandPagePreview);
        var message = JsonSerializer.Serialize(new
        {
            id = "task-preview-1",
            type = "task.get",
            payload = new { taskId = taskId.Value }
        });

        using var response = JsonDocument.Parse(await router.HandleAsync(message));

        var result = response.RootElement.GetProperty("data").GetProperty("brandPagePreviewResult");
        Assert.Equal("demo", result.GetProperty("brandId").GetString());
        Assert.Equal("page_layout.preview.png", result.GetProperty("fileName").GetString());
        Assert.Equal(2588, result.GetProperty("width").GetInt32());
    }

    [Fact]
    public async Task TaskDetailMapsBookDataValidationAndProcessingResults()
    {
        var router = CreateRouter(out var manager);
        var snapshot = new WorkspaceSnapshot(
            Path.GetFullPath("application-root"),
            null,
            null,
            [],
            [],
            DateTimeOffset.UtcNow);
        var validation = new BookDataValidationResult(
            new BookDataValidationState(BookDataValidationStatus.Validated, ContentHash: "sha256:data"),
            []);
        var validationTaskId = manager.AddCompleted(
            new BookDataValidationTaskResult(snapshot, validation),
            BackgroundTaskKind.BookDataValidation);
        using var validationResponse = JsonDocument.Parse(await router.HandleAsync(JsonSerializer.Serialize(new
        {
            id = "task-data",
            type = "task.get",
            payload = new { taskId = validationTaskId.Value }
        })));
        Assert.Equal(
            "sha256:data",
            validationResponse.RootElement.GetProperty("data").GetProperty("bookDataValidationResult")
                .GetProperty("state").GetProperty("contentHash").GetString());

        var processedAt = DateTimeOffset.UtcNow;
        var processing = new BookProcessingResult(
            "book-one",
            "demo",
            "output/book-one.interior.pdf",
            "output/answer",
            1,
            0,
            0,
            1,
            100,
            [],
            processedAt);
        var processingTaskId = manager.AddCompleted(
            new BookProcessingTaskResult(snapshot, processing),
            BackgroundTaskKind.BookProcessing);
        using var processingResponse = JsonDocument.Parse(await router.HandleAsync(JsonSerializer.Serialize(new
        {
            id = "task-processing",
            type = "task.get",
            payload = new { taskId = processingTaskId.Value }
        })));
        Assert.Equal(
            "book-one",
            processingResponse.RootElement.GetProperty("data").GetProperty("bookProcessingResult")
                .GetProperty("bookId").GetString());
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
        return CreateRouter(out manager, out _);
    }

    private static WebViewBridgeRouter CreateRouter(
        out StubTaskManager manager,
        out StubBrandFolderActionService folderAction)
    {
        return CreateRouter(out manager, out folderAction, out _);
    }

    private static WebViewBridgeRouter CreateRouter(
        out StubTaskManager manager,
        out StubBrandFolderActionService folderAction,
        out StubBookOutputFolderActionService outputFolderAction)
    {
        manager = new StubTaskManager();
        folderAction = new StubBrandFolderActionService();
        outputFolderAction = new StubBookOutputFolderActionService();
        return new WebViewBridgeRouter(
            new StubApplicationInfoProvider(),
            manager,
            new StubRootProvider(Path.GetFullPath("application-root")),
            folderAction,
            outputFolderAction);
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

        public BackgroundTaskId AddCompleted(
            object result,
            BackgroundTaskKind kind = BackgroundTaskKind.BrandValidation)
        {
            var taskId = BackgroundTaskId.New();
            tasks.Add(taskId, new BackgroundTaskSnapshot(
                taskId,
                kind,
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

    private sealed class StubBrandFolderActionService : IBrandFolderActionService
    {
        public string? RootPath { get; private set; }

        public string? BrandId { get; private set; }

        public ValueTask OpenAsync(
            string rootPath,
            string brandId,
            CancellationToken cancellationToken = default)
        {
            RootPath = rootPath;
            BrandId = brandId;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubBookOutputFolderActionService : IBookOutputFolderActionService
    {
        public string? RootPath { get; private set; }

        public string? BookId { get; private set; }

        public ValueTask OpenAsync(
            string rootPath,
            string bookId,
            CancellationToken cancellationToken = default)
        {
            RootPath = rootPath;
            BookId = bookId;
            return ValueTask.CompletedTask;
        }
    }
}
