using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using WordSearchBook.Core.Application;
using WordSearchBook.Core.Application.BackgroundTasks;
using WordSearchBook.Core.Application.Workspace;
using WordSearchBook.Core.WordSearch.Application;
using WordSearchBook.Core.WordSearch.Domain;
using WordSearchBook.Core.WordSearch.Validation;

namespace WordSearchBook.Desktop.Bridge;

public sealed class WebViewBridgeRouter(
    IApplicationInfoProvider applicationInfoProvider,
    IBackgroundTaskManager taskManager,
    IApplicationRootProvider rootProvider,
    IBrandFolderActionService brandFolderActionService,
    IBookOutputFolderActionService bookOutputFolderActionService)
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
                "book.data.validate" => await ValidateBookDataAsync(request, cancellationToken),
                "book.process" => await StartProcessingAsync(request, cancellationToken),
                "book.output.open" => await OpenBookOutputAsync(request, cancellationToken),
                "book.brand.assign" => await SaveAssignmentAsync(request, cancellationToken),
                "brand.create" => await CreateBrandAsync(request, cancellationToken),
                "brand.validate" => await ValidateBrandAsync(request, cancellationToken),
                "brand.preview.draw" => await DrawBrandPreviewAsync(request, cancellationToken),
                "brand.folder.open" => await OpenBrandFolderAsync(request, cancellationToken),
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
        catch (BrandFolderActionException exception)
        {
            return Serialize(Failure(request.Id, exception.Code, exception.Message));
        }
        catch (BookOutputFolderActionException exception)
        {
            return Serialize(Failure(request.Id, exception.Code, exception.Message));
        }
        catch (ObjectDisposedException)
        {
            return Serialize(Failure(request.Id, "desktop_unavailable", "The desktop task service is shutting down."));
        }
    }

    private async ValueTask<BridgeResponse> ValidateBookDataAsync(
        BridgeRequest request,
        CancellationToken cancellationToken)
    {
        var bookId = ReadSafeBookId(request.Payload);
        var task = await taskManager.StartAsync(
            BackgroundTaskKind.BookDataValidation,
            $"book-data:{bookId}",
            bookId,
            new BookDataValidationRequest(rootProvider.RootPath, bookId),
            cancellationToken);
        return Success(request.Id!, "background.task", BackgroundTaskBridgeSnapshot.From(task));
    }

    private async ValueTask<BridgeResponse> StartProcessingAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        var (bookId, brandId) = ReadBookAndBrand(request.Payload);
        var task = await taskManager.StartAsync(
            BackgroundTaskKind.BookProcessing,
            $"book-process:{bookId}",
            bookId,
            new BookProcessingTaskRequest(rootProvider.RootPath, bookId, brandId),
            cancellationToken);
        return Success(request.Id!, "background.task", BackgroundTaskBridgeSnapshot.From(task));
    }

    private async ValueTask<BridgeResponse> OpenBookOutputAsync(
        BridgeRequest request,
        CancellationToken cancellationToken)
    {
        var bookId = ReadSafeBookId(request.Payload);
        await bookOutputFolderActionService.OpenAsync(rootProvider.RootPath, bookId, cancellationToken);
        return Success(request.Id!, "book.output.opened", new BookOutputFolderOpened(bookId));
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

    private async ValueTask<BridgeResponse> CreateBrandAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        if (request.Payload is not { ValueKind: JsonValueKind.Object } payload ||
            !payload.TryGetProperty("brandId", out var brandValue))
        {
            throw new ArgumentException("brandId is required.");
        }

        var brandId = brandValue.GetString();
        ArgumentException.ThrowIfNullOrWhiteSpace(brandId);
        var task = await taskManager.StartAsync(
            BackgroundTaskKind.BrandCreate,
            brandId,
            brandId,
            new BrandCreateTaskRequest(rootProvider.RootPath, brandId),
            cancellationToken);
        return Success(request.Id!, "background.task", BackgroundTaskBridgeSnapshot.From(task));
    }

    private async ValueTask<BridgeResponse> ValidateBrandAsync(
        BridgeRequest request,
        CancellationToken cancellationToken)
    {
        var brandId = ReadSafeBrandId(request.Payload);
        var task = await taskManager.StartAsync(
            BackgroundTaskKind.BrandValidation,
            $"brand-validation:{brandId}",
            brandId,
            new BrandValidationRequest(rootProvider.RootPath, brandId),
            cancellationToken);
        return Success(request.Id!, "background.task", BackgroundTaskBridgeSnapshot.From(task));
    }

    private async ValueTask<BridgeResponse> DrawBrandPreviewAsync(
        BridgeRequest request,
        CancellationToken cancellationToken)
    {
        var brandId = ReadSafeBrandId(request.Payload);
        var task = await taskManager.StartAsync(
            BackgroundTaskKind.BrandPagePreview,
            $"brand-preview:{brandId}",
            brandId,
            new BrandPagePreviewTaskRequest(rootProvider.RootPath, brandId),
            cancellationToken);
        return Success(request.Id!, "background.task", BackgroundTaskBridgeSnapshot.From(task));
    }

    private async ValueTask<BridgeResponse> OpenBrandFolderAsync(
        BridgeRequest request,
        CancellationToken cancellationToken)
    {
        var brandId = ReadSafeBrandId(request.Payload);
        await brandFolderActionService.OpenAsync(rootProvider.RootPath, brandId, cancellationToken);
        return Success(request.Id!, "brand.folder.opened", new BrandFolderOpened(brandId));
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
        BrandValidationResult? brandValidationResult = null;
        BrandPagePreviewResult? brandPagePreviewResult = null;
        BookDataValidationResult? bookDataValidationResult = null;
        BookProcessingResult? bookProcessingResult = null;
        if (task.State == BackgroundTaskState.Completed)
        {
            if (taskManager.TryGetResult<BrandValidationTaskResult>(taskId, out var validationTaskResult))
            {
                result = validationTaskResult!.Snapshot;
                brandValidationResult = validationTaskResult.Validation;
            }
            else if (taskManager.TryGetResult<BrandPagePreviewResult>(taskId, out var previewResult))
            {
                brandPagePreviewResult = previewResult;
            }
            else if (taskManager.TryGetResult<BookDataValidationTaskResult>(taskId, out var dataValidationTaskResult))
            {
                result = dataValidationTaskResult!.Snapshot;
                bookDataValidationResult = dataValidationTaskResult.Validation;
            }
            else if (taskManager.TryGetResult<BookProcessingTaskResult>(taskId, out var processingTaskResult))
            {
                result = processingTaskResult!.Snapshot;
                bookProcessingResult = processingTaskResult.Processing;
            }
            else
            {
                taskManager.TryGetResult(taskId, out result);
            }
        }

        return Success(
            request.Id!,
            "background.task.detail",
            new BackgroundTaskDetail(
                BackgroundTaskBridgeSnapshot.From(task),
                result,
                brandValidationResult,
                brandPagePreviewResult,
                bookDataValidationResult,
                bookProcessingResult));
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
        ValidateSafeSegment(bookId, "bookId");
        ValidateSafeSegment(brandId, "brandId");
        return (bookId, brandId);
    }

    private static string ReadSafeBookId(JsonElement? payload)
    {
        if (payload is not { ValueKind: JsonValueKind.Object } value ||
            !value.TryGetProperty("bookId", out var bookValue))
        {
            throw new ArgumentException("bookId is required.");
        }

        var bookId = bookValue.GetString();
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        ValidateSafeSegment(bookId, "bookId");
        return bookId;
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

    private static string ReadSafeBrandId(JsonElement? payload)
    {
        if (payload is not { ValueKind: JsonValueKind.Object } value ||
            !value.TryGetProperty("brandId", out var brandValue))
        {
            throw new ArgumentException("brandId is required.");
        }

        var brandId = brandValue.GetString();
        ArgumentException.ThrowIfNullOrWhiteSpace(brandId);
        ValidateSafeSegment(brandId, "brandId");
        return brandId;
    }

    private static void ValidateSafeSegment(string value, string name)
    {
        if (value is "." or ".." ||
            value.Contains('/') ||
            value.Contains('\\') ||
            value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException($"{name} must be a single safe path segment.");
        }
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
