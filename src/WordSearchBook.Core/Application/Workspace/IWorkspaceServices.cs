namespace WordSearchBook.Core.Application.Workspace;

public interface IWorkspaceSnapshotService
{
    Task<WorkspaceSnapshot> RefreshAsync(string rootPath, CancellationToken cancellationToken = default);
}

public interface IBookBrandAssignmentStore
{
    Task<IReadOnlyDictionary<string, string>> ReadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(string bookId, string brandId, CancellationToken cancellationToken = default);
}
