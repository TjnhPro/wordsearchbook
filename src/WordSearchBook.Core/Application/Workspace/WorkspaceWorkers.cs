using WordSearchBook.Core.Application.BackgroundTasks;
using WordSearchBook.Core.WordSearch.Application;
using WordSearchBook.Core.WordSearch.Contracts;

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
