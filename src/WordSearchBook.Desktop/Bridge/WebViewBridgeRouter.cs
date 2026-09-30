using System.Text.Json;
using System.Text.Json.Serialization;
using WordSearchBook.Core.Application;
using WordSearchBook.Core.Application.BackgroundTasks;
using WordSearchBook.Core.Application.Workspace;
using WordSearchBook.Core.WordSearch.Domain;

namespace WordSearchBook.Desktop.Bridge;

public sealed class WebViewBridgeRouter(
    IApplicationInfoProvider applicationInfoProvider,
    IBackgroundTaskManager taskManager,
    IApplicationRootProvider rootProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    internal async ValueTask<string> HandleAsync(string? rawMessage, CancellationToken cancellationToken = default)
    {
        var request = Parse(rawMessage);
        if (request is null || string.IsNullOrWhiteSpace(request.Id) || string.IsNullOrWhiteSpace(request.Type))
        {
            return Serialize(Failure(request?.Id, "malformed_message", "The bridge message must contain non-empty id and type fields."));
        }

        try
        {
            var response = request.Type switch
            {
                "ping" => Success(request.Id, "pong", applicationInfoProvider.GetCurrent()),
                "workspace.refresh" => Success(
                    request.Id,
                    "background.task",
                    BackgroundTaskBridgeSnapshot.From(await taskManager.StartAsync(
                        BackgroundTaskKind.WorkspaceRefresh,
                        "workspace",
                        "Workspace",
                        new WorkspaceRefreshRequest(rootProvider.RootPath),
                        cancellationToken))),
                "book.generate" => await StartGenerationAsync(request, cancellationToken),
                "book.brand.assign" => await SaveAssignmentAsync(request, cancellationToken),
                "settings.global.save" => await SaveGlobalSettingsAsync(request, cancellationToken),
                "settings.brand.save" => await SaveBrandSettingsAsync(request, cancellationToken),
                "task.list" => Success(
                    request.Id,
                    "background.tasks",
                    (await taskManager.ListAsync(cancellationToken)).Select(BackgroundTaskBridgeSnapshot.From).ToArray()),
                "task.get" => await GetTaskAsync(request, cancellationToken),
                "task.cancel" => await CancelTaskAsync(request, cancellationToken),
                _ => Failure(request.Id, "unsupported_message", $"Message type '{request.Type}' is not supported.")
            };
            return Serialize(response);
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException)
        {
            return Serialize(Failure(request.Id, "invalid_payload", "The bridge message payload is invalid."));
        }
        catch (ObjectDisposedException)
        {
            return Serialize(Failure(request.Id, "desktop_unavailable", "The desktop task service is shutting down."));
        }
    }

    private async ValueTask<BridgeResponse> StartGenerationAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        var (bookId, brandId) = ReadBookAndBrand(request.Payload);
        var task = await taskManager.StartAsync(
            BackgroundTaskKind.BookGeneration,
            $"{bookId}:{brandId}",
            bookId,
            new BookGenerationTaskRequest(rootProvider.RootPath, bookId, brandId),
            cancellationToken);
        return Success(request.Id!, "background.task", BackgroundTaskBridgeSnapshot.From(task));
    }

    private async ValueTask<BridgeResponse> SaveAssignmentAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        var (bookId, brandId) = ReadBookAndBrand(request.Payload);
        var task = await taskManager.StartAsync(
            BackgroundTaskKind.BookBrandAssignmentSave,
            bookId,
            bookId,
            new BookBrandAssignmentTaskRequest(rootProvider.RootPath, bookId, brandId),
            cancellationToken);
        return Success(request.Id!, "background.task", BackgroundTaskBridgeSnapshot.From(task));
    }

    private async ValueTask<BridgeResponse> SaveGlobalSettingsAsync(
        BridgeRequest request,
        CancellationToken cancellationToken)
    {
        var settings = ReadSettings<GlobalWordSearchSettings>(request.Payload);
        var task = await taskManager.StartAsync(
            BackgroundTaskKind.SettingsSave,
            "global",
            "Global",
            new GlobalSettingsSaveTaskRequest(rootProvider.RootPath, settings),
            cancellationToken);
        return Success(request.Id!, "background.task", BackgroundTaskBridgeSnapshot.From(task));
    }

    private async ValueTask<BridgeResponse> SaveBrandSettingsAsync(
        BridgeRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Payload is not { ValueKind: JsonValueKind.Object } payload ||
            !payload.TryGetProperty("brandId", out var brandValue))
        {
            throw new ArgumentException("brandId is required.");
        }

        var brandId = brandValue.GetString();
        ArgumentException.ThrowIfNullOrWhiteSpace(brandId);
        var settings = ReadSettings<BrandWordSearchSettings>(request.Payload);
        var task = await taskManager.StartAsync(
            BackgroundTaskKind.SettingsSave,
            $"brand:{brandId}",
            brandId,
            new BrandSettingsSaveTaskRequest(rootProvider.RootPath, brandId, settings),
            cancellationToken);
        return Success(request.Id!, "background.task", BackgroundTaskBridgeSnapshot.From(task));
    }

    private async ValueTask<BridgeResponse> GetTaskAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        var taskId = ReadTaskId(request.Payload);
        var task = await taskManager.GetAsync(taskId, cancellationToken);
        if (task is null)
        {
            return Failure(request.Id, "task_not_found", "The background task was not found.");
        }

        WorkspaceSnapshot? result = null;
        if (task.State == BackgroundTaskState.Completed)
        {
            taskManager.TryGetResult(taskId, out result);
        }

        return Success(request.Id!, "background.task.detail", new BackgroundTaskDetail(BackgroundTaskBridgeSnapshot.From(task), result));
    }

    private async ValueTask<BridgeResponse> CancelTaskAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        var task = await taskManager.CancelAsync(ReadTaskId(request.Payload), cancellationToken);
        return task is null
            ? Failure(request.Id, "task_not_found", "The background task was not found.")
            : Success(request.Id!, "background.task", BackgroundTaskBridgeSnapshot.From(task));
    }

    private static (string BookId, string BrandId) ReadBookAndBrand(JsonElement? payload)
    {
        if (payload is not { ValueKind: JsonValueKind.Object } value ||
            !value.TryGetProperty("bookId", out var bookValue) ||
            !value.TryGetProperty("brandId", out var brandValue))
        {
            throw new ArgumentException("bookId and brandId are required.");
        }

        var bookId = bookValue.GetString();
        var brandId = brandValue.GetString();
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        ArgumentException.ThrowIfNullOrWhiteSpace(brandId);
        return (bookId, brandId);
    }

    private static BackgroundTaskId ReadTaskId(JsonElement? payload)
    {
        if (payload is not { ValueKind: JsonValueKind.Object } value ||
            !value.TryGetProperty("taskId", out var taskValue) ||
            !BackgroundTaskId.TryParse(taskValue.GetString(), out var taskId))
        {
            throw new ArgumentException("A valid taskId is required.");
        }

        return taskId;
    }

    private static T ReadSettings<T>(JsonElement? payload)
    {
        if (payload is not { ValueKind: JsonValueKind.Object } value ||
            !value.TryGetProperty("settings", out var settingsValue))
        {
            throw new ArgumentException("settings are required.");
        }

        return settingsValue.Deserialize<T>(JsonOptions)
            ?? throw new ArgumentException("settings are required.");
    }

    private static BridgeRequest? Parse(string? rawMessage)
    {
        try
        {
            return string.IsNullOrWhiteSpace(rawMessage) ? null : JsonSerializer.Deserialize<BridgeRequest>(rawMessage, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static BridgeResponse Success(string id, string type, object data) => new(id, type, true, data);
    private static BridgeResponse Failure(string? id, string code, string message) => new(id, "error", false, Error: new BridgeError(code, message));
    private static string Serialize(BridgeResponse response) => JsonSerializer.Serialize(response, JsonOptions);
}
