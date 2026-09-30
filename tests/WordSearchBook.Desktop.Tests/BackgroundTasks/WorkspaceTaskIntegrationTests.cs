using Microsoft.Extensions.DependencyInjection;
using System.IO;
using WordSearchBook.Core.Application.BackgroundTasks;
using WordSearchBook.Core.Application.Workspace;
using WordSearchBook.Desktop.BackgroundTasks;
using WordSearchBook.Infrastructure.DependencyInjection;
using WordSearchBook.Infrastructure.WordSearch.Settings;

namespace WordSearchBook.Desktop.Tests.BackgroundTasks;

public sealed class WorkspaceTaskIntegrationTests
{
    [Fact]
    public async Task EnqueuedBrandCreateWritesDefaultsAndReturnsFreshWorkspaceSnapshot()
    {
        var root = await CopyFixtureToTemporaryRootAsync();
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
            Assert.True(File.Exists(Path.Combine(root, "brands", "new-brand", "page_layout.png")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task EnqueuedValidationAndProcessingPublishOutputAndReturnFreshWorkspaceSnapshot()
    {
        var root = await CopyFixtureToTemporaryRootAsync();
        try
        {
            var services = new ServiceCollection();
            services.AddWordSearchBookInfrastructure();
            services.AddSingleton<IBookBrandAssignmentStore>(new EmptyAssignmentStore());
            using var provider = services.BuildServiceProvider();
            using var manager = new BackgroundTaskManager(provider);
            var validationTask = await manager.StartAsync(
                BackgroundTaskKind.BrandValidation,
                "brand-layout:demo",
                "demo",
                new BrandValidationRequest(root, "demo"));
            Assert.True(await manager.WaitAsync(validationTask.TaskId, TimeSpan.FromSeconds(10)));
            Assert.Equal(BackgroundTaskState.Completed, (await manager.GetAsync(validationTask.TaskId))!.State);
            Assert.True(manager.TryGetResult<BrandValidationTaskResult>(validationTask.TaskId, out var validationResult));
            Assert.True(validationResult!.Validation.IsSuccess);

            var dataValidationTask = await manager.StartAsync(
                BackgroundTaskKind.BookDataValidation,
                "book-data:sample-book",
                "sample-book",
                new BookDataValidationRequest(root, "sample-book"));
            Assert.True(await manager.WaitAsync(dataValidationTask.TaskId, TimeSpan.FromSeconds(10)));
            Assert.True(manager.TryGetResult<BookDataValidationTaskResult>(dataValidationTask.TaskId, out var dataValidationResult));
            Assert.True(dataValidationResult!.Validation.IsSuccess);

            var task = await manager.StartAsync(
                BackgroundTaskKind.BookProcessing,
                "book-process:sample-book",
                "sample-book",
                new BookProcessingTaskRequest(root, "sample-book", "demo"));

            Assert.True(await manager.WaitAsync(task.TaskId, TimeSpan.FromSeconds(30)));
            var completed = await manager.GetAsync(task.TaskId);
            Assert.Equal(BackgroundTaskState.Completed, completed!.State);
            Assert.True(manager.TryGetResult<BookProcessingTaskResult>(task.TaskId, out var processingResult));
            var book = Assert.Single(processingResult!.Snapshot.Books);
            Assert.Contains("demo", book.CachedBrandIds);
            Assert.Equal(BookOutputStatus.Ready, book.Output!.Status);
            Assert.True(File.Exists(Path.Combine(root, "input", "sample-book", ".workspace", "cache", "demo", "manifest.json")));
            Assert.True(File.Exists(Path.Combine(root, "input", "sample-book", "output", "sample-book.interior.pdf")));
            Assert.True(File.Exists(Path.Combine(root, "input", "sample-book", "output", "answer", "001.jpg")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task EnqueuedBrandPagePreviewPublishesResultBesideLayout()
    {
        var root = await CopyFixtureToTemporaryRootAsync();
        try
        {
            var services = new ServiceCollection();
            services.AddWordSearchBookInfrastructure();
            using var provider = services.BuildServiceProvider();
            using var manager = new BackgroundTaskManager(provider);

            var task = await manager.StartAsync(
                BackgroundTaskKind.BrandPagePreview,
                "brand-preview:demo",
                "demo",
                new BrandPagePreviewTaskRequest(root, "demo"));

            Assert.True(await manager.WaitAsync(task.TaskId, TimeSpan.FromSeconds(15)));
            var completed = await manager.GetAsync(task.TaskId);
            Assert.Equal(BackgroundTaskState.Completed, completed!.State);
            Assert.True(manager.TryGetResult<WordSearchBook.Core.WordSearch.Application.BrandPagePreviewResult>(
                task.TaskId,
                out var result));
            Assert.Equal("page_layout.preview.png", result!.FileName);
            Assert.True(File.Exists(Path.Combine(root, "brands", "demo", result.FileName)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<string> CopyFixtureToTemporaryRootAsync()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "TestData", "SingleTopicBook");
        var destination = Path.Combine(Path.GetTempPath(), $"word-search-task-{Guid.NewGuid():N}");
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(source, file);
            var segments = relativePath.Split(Path.DirectorySeparatorChar);
            if (segments.Contains(".workspace", StringComparer.OrdinalIgnoreCase) ||
                segments.Contains("output", StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var target = file.Replace(source, destination, StringComparison.Ordinal);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        const string layoutTemplateBrand = "layout-template";
        await new JsonWordSearchSettingsWriter(new JsonWordSearchSettingsReader())
            .CreateBrandAsync(destination, layoutTemplateBrand);
        var templateDirectory = Path.Combine(destination, "brands", layoutTemplateBrand);
        File.Move(
            Path.Combine(templateDirectory, "page_layout.png"),
            Path.Combine(destination, "brands", "demo", "page_layout.png"));
        Directory.Delete(templateDirectory, recursive: true);

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
