using WordSearchBook.Core.Application.BackgroundTasks;

namespace WordSearchBook.Desktop.Tests;

public sealed class MainWindowTests
{
    [Theory]
    [InlineData(BackgroundTaskState.Queued, true)]
    [InlineData(BackgroundTaskState.Running, true)]
    [InlineData(BackgroundTaskState.Cancelling, true)]
    [InlineData(BackgroundTaskState.Completed, false)]
    [InlineData(BackgroundTaskState.Failed, false)]
    [InlineData(BackgroundTaskState.Cancelled, false)]
    public void DetectsWhetherCloseRequiresBackgroundTaskConfirmation(
        BackgroundTaskState state,
        bool expected)
    {
        var task = new BackgroundTaskSnapshot(
            BackgroundTaskId.New(),
            BackgroundTaskKind.WorkspaceRefresh,
            state,
            "workspace",
            "Workspace",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null);

        Assert.Equal(expected, MainWindow.HasActiveTasks([task]));
    }
}
