namespace WordSearchBook.Core.Application.BackgroundTasks;

public readonly record struct BackgroundTaskId(string Value)
{
    public static BackgroundTaskId New() => new(Guid.NewGuid().ToString("N"));

    public static bool TryParse(string? value, out BackgroundTaskId taskId)
    {
        if (!string.IsNullOrWhiteSpace(value) && Guid.TryParseExact(value, "N", out _))
        {
            taskId = new BackgroundTaskId(value);
            return true;
        }

        taskId = default;
        return false;
    }
}

public enum BackgroundTaskKind
{
    WorkspaceRefresh,
    BookGeneration,
    SettingsSave,
    BookBrandAssignmentSave,
    BrandCreate,
    BrandPageLayoutValidation,
    BrandPagePreview
}

public enum BackgroundTaskState
{
    Queued,
    Running,
    Cancelling,
    Completed,
    Failed,
    Cancelled
}

public sealed record BackgroundTaskSnapshot(
    BackgroundTaskId TaskId,
    BackgroundTaskKind Kind,
    BackgroundTaskState State,
    string Key,
    string? Subject,
    string? Step,
    int? Completed,
    int? Total,
    string? Detail,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? ErrorCode,
    string? ErrorMessage);

public sealed class BackgroundTaskFailureException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
