using System.Text.Json;
using WordSearchBook.Core.Application.BackgroundTasks;
using WordSearchBook.Core.Application.Workspace;
using WordSearchBook.Core.WordSearch.Application;
using WordSearchBook.Core.WordSearch.Validation;

namespace WordSearchBook.Desktop.Bridge;

internal sealed record BridgeRequest(string? Id, string? Type, JsonElement? Payload = null);
internal sealed record BridgeError(string Code, string Message);
internal sealed record BridgeResponse(string? Id, string Type, bool Ok, object? Data = null, BridgeError? Error = null);

internal sealed record BackgroundTaskBridgeSnapshot(
    string TaskId,
    string Kind,
    string State,
    string Key,
    string? Subject,
    string? Step,
    int? Completed,
    int? Total,
    string? Detail,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static BackgroundTaskBridgeSnapshot From(BackgroundTaskSnapshot snapshot) => new(
        snapshot.TaskId.Value,
        snapshot.Kind.ToString(),
        snapshot.State.ToString(),
        snapshot.Key,
        snapshot.Subject,
        snapshot.Step,
        snapshot.Completed,
        snapshot.Total,
        snapshot.Detail,
        snapshot.StartedAt,
        snapshot.FinishedAt,
        snapshot.ErrorCode,
        snapshot.ErrorMessage);
}

internal sealed record BackgroundTaskDetail(
    BackgroundTaskBridgeSnapshot Task,
    WorkspaceSnapshot? Result,
    BrandValidationResult? BrandValidationResult = null,
    BrandPagePreviewResult? BrandPagePreviewResult = null,
    BookDataValidationResult? BookDataValidationResult = null,
    BookProcessingResult? BookProcessingResult = null);

internal sealed record BrandFolderOpened(string BrandId);

internal sealed record BookOutputFolderOpened(string BookId);
