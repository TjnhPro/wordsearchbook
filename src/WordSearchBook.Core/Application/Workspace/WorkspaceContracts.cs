using WordSearchBook.Core.WordSearch.Domain;
using WordSearchBook.Core.WordSearch.Application;
using WordSearchBook.Core.WordSearch.Validation;

namespace WordSearchBook.Core.Application.Workspace;

public sealed record WorkspaceIssue(string Code, string Message);

public enum BookOutputStatus
{
    Missing,
    Ready,
    Stale,
    Unavailable
}

public sealed record WorkspaceBookOutput(
    BookOutputStatus Status,
    string? BrandId = null,
    string? PdfFileName = null,
    long PdfLengthBytes = 0,
    int PuzzlePageCount = 0,
    int FrontPageCount = 0,
    int BackPageCount = 0,
    int PdfPageCount = 0,
    int AnswerCount = 0,
    long AnswerLengthBytes = 0,
    DateTimeOffset? ProcessedAtUtc = null,
    string? ReasonCode = null);

public sealed record WorkspaceBrandAssetFile(
    string Name,
    string RelativePath,
    string Extension,
    BrandValidationStatus Status);

public sealed record WorkspaceBrandAssetFolder(
    string Key,
    string RelativePath,
    bool Exists,
    IReadOnlyList<WorkspaceBrandAssetFile> Files);

public sealed record WorkspaceBrandOptionalFile(
    string Key,
    string RelativePath,
    bool Exists,
    string Extension,
    BrandValidationStatus Status);

public sealed record WorkspaceBrand(
    string Id,
    BrandWordSearchSettings? Settings,
    BrandValidationState Validation,
    WorkspaceIssue? Issue,
    IReadOnlyList<WorkspaceBrandAssetFolder> AssetFolders,
    bool SettingsRequireSave = false,
    string? SettingsUpdateReasonCode = null,
    WorkspaceBrandOptionalFile? QrPage = null);

public sealed record WorkspaceBook(
    string Id,
    int TopicCount,
    string? SelectedBrandId,
    IReadOnlyList<string> CachedBrandIds,
    WorkspaceIssue? Issue,
    BookDataValidationState? DataValidation = null,
    WorkspaceBookOutput? Output = null);

public sealed record WorkspaceSnapshot(
    string RootPath,
    GlobalWordSearchSettings? GlobalSettings,
    WorkspaceIssue? GlobalSettingsIssue,
    IReadOnlyList<WorkspaceBrand> Brands,
    IReadOnlyList<WorkspaceBook> Books,
    DateTimeOffset RefreshedAt);

public sealed record WorkspaceRefreshRequest(string RootPath);

public sealed record BookGenerationTaskRequest(string RootPath, string BookId, string BrandId);

public sealed record BookProcessingTaskRequest(string RootPath, string BookId, string BrandId);

public sealed record BookProcessingTaskResult(
    WorkspaceSnapshot Snapshot,
    BookProcessingResult Processing);

public sealed record BookDataValidationRequest(string RootPath, string BookId);

public sealed record BookDataValidationTaskResult(
    WorkspaceSnapshot Snapshot,
    BookDataValidationResult Validation);

public sealed record BookBrandAssignmentTaskRequest(string RootPath, string BookId, string BrandId);

public sealed record BrandCreateTaskRequest(string RootPath, string BrandId);

public sealed record BrandValidationRequest(string RootPath, string BrandId);

public sealed record BrandValidationTaskResult(
    WorkspaceSnapshot Snapshot,
    BrandValidationResult Validation);

public sealed record BrandPagePreviewTaskRequest(string RootPath, string BrandId);

public abstract record SettingsSaveTaskRequest(string RootPath);

public sealed record GlobalSettingsSaveTaskRequest(
    string RootPath,
    GlobalWordSearchSettings Settings) : SettingsSaveTaskRequest(RootPath);

public sealed record BrandSettingsSaveTaskRequest(
    string RootPath,
    string BrandId,
    BrandWordSearchSettings Settings) : SettingsSaveTaskRequest(RootPath);
