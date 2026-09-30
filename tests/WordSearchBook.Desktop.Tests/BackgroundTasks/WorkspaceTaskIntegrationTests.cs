using Microsoft.Extensions.DependencyInjection;
using System.IO;
using WordSearchBook.Core.Application.BackgroundTasks;
using WordSearchBook.Core.Application.Workspace;
using WordSearchBook.Desktop.BackgroundTasks;
using WordSearchBook.Infrastructure.DependencyInjection;

namespace WordSearchBook.Desktop.Tests.BackgroundTasks;

public sealed class WorkspaceTaskIntegrationTests
{
    [Fact]
    public async Task EnqueuedBrandCreateWritesDefaultsAndReturnsFreshWorkspaceSnapshot()
    {
        var root = CopyFixtureToTemporaryRoot();
        try
        {
            var services = new ServiceCollection();
            services.AddWordSearchBookInfrastructure();
            services.AddSingleton<IBookBrandAssignmentStore>(new EmptyAssignmentStore());
            using var provider = services.BuildServiceProvider();
            using var manager = new BackgroundTaskManager(provider);

            var task = await manager.StartAsync(
                BackgroundTaskKind.BrandCreate,
                "new-brand",
                "new-brand",
                new BrandCreateTaskRequest(root, "new-brand"));

            Assert.True(await manager.WaitAsync(task.TaskId, TimeSpan.FromSeconds(10)));
            var completed = await manager.GetAsync(task.TaskId);
            Assert.Equal(BackgroundTaskState.Completed, completed!.State);
            Assert.True(manager.TryGetResult<WorkspaceSnapshot>(task.TaskId, out var snapshot));
            var brand = Assert.Single(snapshot!.Brands, candidate => candidate.Id == "new-brand");
            Assert.Equal(2000, brand.Settings!.BoardGame.Rectangle.Width);
            Assert.True(File.Exists(Path.Combine(root, "brands", "new-brand", "settings.json")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task EnqueuedGenerationProducesCacheAndReturnsFreshWorkspaceSnapshot()
    {
        var root = CopyFixtureToTemporaryRoot();
        try
        {
            var services = new ServiceCollection();
            services.AddWordSearchBookInfrastructure();
            services.AddSingleton<IBookBrandAssignmentStore>(new EmptyAssignmentStore());
            using var provider = services.BuildServiceProvider();
            using var manager = new BackgroundTaskManager(provider);

            var task = await manager.StartAsync(
                BackgroundTaskKind.BookGeneration,
                "sample-book:demo",
                "sample-book",
                new BookGenerationTaskRequest(root, "sample-book", "demo"));

            Assert.True(await manager.WaitAsync(task.TaskId, TimeSpan.FromSeconds(10)));
            var completed = await manager.GetAsync(task.TaskId);
            Assert.Equal(BackgroundTaskState.Completed, completed!.State);
            Assert.True(manager.TryGetResult<WorkspaceSnapshot>(task.TaskId, out var snapshot));
            var book = Assert.Single(snapshot!.Books);
            Assert.Contains("demo", book.CachedBrandIds);
            Assert.True(File.Exists(Path.Combine(root, "input", "sample-book", ".workspace", "cache", "demo", "manifest.json")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CopyFixtureToTemporaryRoot()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "TestData", "SingleTopicBook");
        var destination = Path.Combine(Path.GetTempPath(), $"word-search-task-{Guid.NewGuid():N}");
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = file.Replace(source, destination, StringComparison.Ordinal);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        return destination;
    }

    private sealed class EmptyAssignmentStore : IBookBrandAssignmentStore
    {
        public Task<IReadOnlyDictionary<string, string>> ReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());

        public Task SaveAsync(string bookId, string brandId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
