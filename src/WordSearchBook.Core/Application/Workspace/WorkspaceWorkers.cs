using WordSearchBook.Core.Application.BackgroundTasks;
using WordSearchBook.Core.WordSearch.Application;
using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Core.WordSearch.Settings;
using WordSearchBook.Core.WordSearch.Validation;

namespace WordSearchBook.Core.Application.Workspace;

public sealed class WorkspaceRefreshWorker(IWorkspaceSnapshotService snapshotService)
    : BackgroundTaskWorker<WorkspaceRefreshRequest, WorkspaceSnapshot>
{
    public override BackgroundTaskKind Kind => BackgroundTaskKind.WorkspaceRefresh;

    protected override async ValueTask<WorkspaceSnapshot> ExecuteTypedAsync(
        WorkspaceRefreshRequest request,
        IBackgroundTaskContext context,
        CancellationToken cancellationToken)
    {
        context.Report("Scanning workspace", subject: "Workspace");
        return await snapshotService.RefreshAsync(request.RootPath, cancellationToken);
    }
}

public sealed class BookGenerationWorker(
    IWordSearchBookGenerationService generationService,
    IWorkspaceSnapshotService snapshotService)
    : BackgroundTaskWorker<BookGenerationTaskRequest, WorkspaceSnapshot>
{
    public override BackgroundTaskKind Kind => BackgroundTaskKind.BookGeneration;

    protected override async ValueTask<WorkspaceSnapshot> ExecuteTypedAsync(
        BookGenerationTaskRequest request,
        IBackgroundTaskContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            context.Report("Generating boards", subject: request.BookId);
            await generationService.GenerateAsync(
                new WordSearchGenerationRequest(request.RootPath, request.BookId, request.BrandId),
                cancellationToken);
            context.Report("Refreshing workspace", subject: request.BookId);
            return await snapshotService.RefreshAsync(request.RootPath, cancellationToken);
        }
        catch (WordSearchGenerationException exception)
        {
            throw new BackgroundTaskFailureException(exception.Code, exception.Message);
        }
    }
}

public sealed class BookBrandAssignmentWorker(
    IBookBrandAssignmentStore assignmentStore,
    IWorkspaceSnapshotService snapshotService)
    : BackgroundTaskWorker<BookBrandAssignmentTaskRequest, WorkspaceSnapshot>
{
    public override BackgroundTaskKind Kind => BackgroundTaskKind.BookBrandAssignmentSave;

    protected override async ValueTask<WorkspaceSnapshot> ExecuteTypedAsync(
        BookBrandAssignmentTaskRequest request,
        IBackgroundTaskContext context,
        CancellationToken cancellationToken)
    {
        context.Report("Saving brand selection", subject: request.BookId);
        await assignmentStore.SaveAsync(request.BookId, request.BrandId, cancellationToken);
        return await snapshotService.RefreshAsync(request.RootPath, cancellationToken);
    }
}

public sealed class SettingsSaveWorker(
    IWordSearchSettingsWriter settingsWriter,
    IWorkspaceSnapshotService snapshotService)
    : BackgroundTaskWorker<SettingsSaveTaskRequest, WorkspaceSnapshot>
{
    public override BackgroundTaskKind Kind => BackgroundTaskKind.SettingsSave;

    protected override async ValueTask<WorkspaceSnapshot> ExecuteTypedAsync(
        SettingsSaveTaskRequest request,
        IBackgroundTaskContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            context.Report("Saving settings", subject: request is BrandSettingsSaveTaskRequest brandRequest ? brandRequest.BrandId : "Global");
            switch (request)
            {
                case GlobalSettingsSaveTaskRequest global:
                    await settingsWriter.SaveGlobalAsync(global.RootPath, global.Settings, cancellationToken);
                    break;
                case BrandSettingsSaveTaskRequest brand:
                    await settingsWriter.SaveBrandAsync(brand.RootPath, brand.BrandId, brand.Settings, cancellationToken);
                    break;
                default:
                    throw new BackgroundTaskFailureException("settings_request_invalid", "The settings save request is invalid.");
            }

            return await snapshotService.RefreshAsync(request.RootPath, cancellationToken);
        }
        catch (WordSearchGenerationException exception)
        {
            throw new BackgroundTaskFailureException(exception.Code, exception.Message);
        }
    }
}

public sealed class BrandCreateWorker(
    IWordSearchSettingsWriter settingsWriter,
    IWorkspaceSnapshotService snapshotService)
    : BackgroundTaskWorker<BrandCreateTaskRequest, WorkspaceSnapshot>
{
    public override BackgroundTaskKind Kind => BackgroundTaskKind.BrandCreate;

    protected override async ValueTask<WorkspaceSnapshot> ExecuteTypedAsync(
        BrandCreateTaskRequest request,
        IBackgroundTaskContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            context.Report("Creating brand", subject: request.BrandId);
            await settingsWriter.CreateBrandAsync(request.RootPath, request.BrandId, cancellationToken);
            context.Report("Refreshing workspace", subject: request.BrandId);
            return await snapshotService.RefreshAsync(request.RootPath, cancellationToken);
        }
        catch (WordSearchGenerationException exception)
        {
            throw new BackgroundTaskFailureException(exception.Code, exception.Message);
        }
    }
}

public sealed class BrandValidationWorker(
    IBrandValidationService validationService,
    IWorkspaceSnapshotService snapshotService)
    : BackgroundTaskWorker<BrandValidationRequest, BrandValidationTaskResult>
{
    public override BackgroundTaskKind Kind => BackgroundTaskKind.BrandValidation;

    protected override async ValueTask<BrandValidationTaskResult> ExecuteTypedAsync(
        BrandValidationRequest request,
        IBackgroundTaskContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            context.Report("Validating brand assets", subject: request.BrandId);
            var validation = await validationService.ValidateAsync(request.RootPath, request.BrandId, cancellationToken);
            context.Report("Refreshing workspace", subject: request.BrandId);
            var snapshot = await snapshotService.RefreshAsync(request.RootPath, cancellationToken);
            return new BrandValidationTaskResult(snapshot, validation);
        }
        catch (WordSearchGenerationException exception)
        {
            throw new BackgroundTaskFailureException(exception.Code, exception.Message);
        }
    }
}

public sealed class BrandPagePreviewWorker(IBrandPagePreviewService previewService)
    : BackgroundTaskWorker<BrandPagePreviewTaskRequest, BrandPagePreviewResult>
{
    public override BackgroundTaskKind Kind => BackgroundTaskKind.BrandPagePreview;

    protected override async ValueTask<BrandPagePreviewResult> ExecuteTypedAsync(
        BrandPagePreviewTaskRequest request,
        IBackgroundTaskContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            context.Report("Drawing page preview", subject: request.BrandId);
            return await previewService.DrawAsync(request.RootPath, request.BrandId, cancellationToken);
        }
        catch (WordSearchGenerationException exception)
        {
            throw new BackgroundTaskFailureException(exception.Code, exception.Message);
        }
    }
}
