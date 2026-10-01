using WordSearchBook.Core.Application.Workspace;
using WordSearchBook.Core.WordSearch.Validation;
using WordSearchBook.Infrastructure.WordSearch.Settings;
using WordSearchBook.Infrastructure.WordSearch.Validation;
using WordSearchBook.Infrastructure.Workspace;
using System.Text.Json.Nodes;

namespace WordSearchBook.Infrastructure.Tests.Workspace;

public sealed class WordSearchWorkspaceSnapshotServiceTests
{
    [Fact]
    public async Task DiscoversValidBooksBrandsSettingsAndRememberedAssignment()
    {
        var root = CopyFixtureToTemporaryRoot();
        try
        {
            var dataValidation = CreateDataValidationService();
            await dataValidation.ValidateAsync(root, "sample-book");
            var service = new WordSearchWorkspaceSnapshotService(
                dataValidation,
                new JsonWordSearchSettingsReader(),
                CreateValidationService(),
                new StubAssignmentStore(new Dictionary<string, string> { ["sample-book"] = "demo" }),
                new JsonBookOutputSnapshotService());

            var snapshot = await service.RefreshAsync(root);

            Assert.Equal(20, snapshot.GlobalSettings!.Board.Width);
            var brand = Assert.Single(snapshot.Brands);
            Assert.Equal("demo", brand.Id);
            Assert.Null(brand.Issue);
            Assert.Equal(BrandValidationStatus.NotValidated, brand.Validation.Status);
            var book = Assert.Single(snapshot.Books);
            Assert.Equal("sample-book", book.Id);
            Assert.Equal(1, book.TopicCount);
            Assert.Equal("demo", book.SelectedBrandId);
            Assert.Null(book.Issue);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task KeepsInvalidBookAsAnItemLevelIssue()
    {
        var root = CopyFixtureToTemporaryRoot();
        try
        {
            await File.WriteAllLinesAsync(
                Path.Combine(root, "input", "sample-book", "data.csv"),
                ["Topic,Quote,Keyword,Word Search Key", "Broken,Keep going,Only One,ONLYONE"]);
            var dataValidation = CreateDataValidationService();
            await dataValidation.ValidateAsync(root, "sample-book");
            var service = new WordSearchWorkspaceSnapshotService(
                dataValidation,
                new JsonWordSearchSettingsReader(),
                CreateValidationService(),
                new StubAssignmentStore(new Dictionary<string, string>()),
                new JsonBookOutputSnapshotService());

            var snapshot = await service.RefreshAsync(root);

            var book = Assert.Single(snapshot.Books);
            Assert.Equal(1, book.TopicCount);
            Assert.Equal("topic_word_count_invalid", book.Issue!.Code);
            Assert.Single(snapshot.Brands);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExposesDefaultQuoteAndRequiresSaveForLegacyBrandSettings()
    {
        var root = CopyFixtureToTemporaryRoot();
        try
        {
            var settingsPath = Path.Combine(root, "brands", "demo", "settings.json");
            var document = JsonNode.Parse(await File.ReadAllTextAsync(settingsPath))!.AsObject();
            document.Remove("quote");
            await File.WriteAllTextAsync(settingsPath, document.ToJsonString());
            var service = new WordSearchWorkspaceSnapshotService(
                CreateDataValidationService(),
                new JsonWordSearchSettingsReader(),
                CreateValidationService(),
                new StubAssignmentStore(new Dictionary<string, string>()),
                new JsonBookOutputSnapshotService());

            var snapshot = await service.RefreshAsync(root);

            var brand = Assert.Single(snapshot.Brands);
            Assert.True(brand.SettingsRequireSave);
            Assert.NotNull(brand.Settings);
            Assert.Equal(300, brand.Settings.Quote.Rectangle.X);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ListsOnlyTopLevelSupportedBrandImagesInStableFolderOrder()
    {
        var root = CopyFixtureToTemporaryRoot();
        try
        {
            var brand = Path.Combine(root, "brands", "demo");
            var front = Path.Combine(brand, "front");
            var nested = Path.Combine(front, "nested");
            Directory.CreateDirectory(nested);
            Directory.CreateDirectory(Path.Combine(brand, "back"));
            await File.WriteAllTextAsync(Path.Combine(front, "B.JPG"), "metadata only");
            await File.WriteAllTextAsync(Path.Combine(front, "a.png"), "metadata only");
            await File.WriteAllTextAsync(Path.Combine(front, "ignored.txt"), "ignored");
            await File.WriteAllTextAsync(Path.Combine(nested, "nested.png"), "ignored");
            var service = new WordSearchWorkspaceSnapshotService(
                CreateDataValidationService(),
                new JsonWordSearchSettingsReader(),
                CreateValidationService(),
                new StubAssignmentStore(new Dictionary<string, string>()),
                new JsonBookOutputSnapshotService());

            var snapshot = await service.RefreshAsync(root);

            var folders = Assert.Single(snapshot.Brands).AssetFolders;
            Assert.Collection(
                folders,
                folder =>
                {
                    Assert.Equal("front", folder.Key);
                    Assert.True(folder.Exists);
                    Assert.Equal(["a.png", "B.JPG"], folder.Files.Select(file => file.Name));
                    Assert.All(folder.Files, file => Assert.Equal(BrandValidationStatus.NotValidated, file.Status));
                },
                folder =>
                {
                    Assert.Equal("back", folder.Key);
                    Assert.True(folder.Exists);
                    Assert.Empty(folder.Files);
                });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CreatesDefaultGlobalSettingsWhenWorkspaceSettingsAreMissing()
    {
        var root = CopyFixtureToTemporaryRoot();
        try
        {
            File.Delete(Path.Combine(root, "settings.json"));
            var service = new WordSearchWorkspaceSnapshotService(
                CreateDataValidationService(),
                new JsonWordSearchSettingsReader(),
                CreateValidationService(),
                new StubAssignmentStore(new Dictionary<string, string>()),
                new JsonBookOutputSnapshotService());

            var snapshot = await service.RefreshAsync(root);

            Assert.NotNull(snapshot.GlobalSettings);
            Assert.Null(snapshot.GlobalSettingsIssue);
            Assert.Equal(20, snapshot.GlobalSettings.Board.Width);
            Assert.Equal(2588, snapshot.GlobalSettings.Page.Width);
            Assert.True(File.Exists(Path.Combine(root, "settings.json")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task KeepsBrandValidationVisibleWhenGlobalSettingsAreInvalid()
    {
        var root = CopyFixtureToTemporaryRoot();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "settings.json"), "{}");
            var service = new WordSearchWorkspaceSnapshotService(
                CreateDataValidationService(),
                new JsonWordSearchSettingsReader(),
                CreateValidationService(),
                new StubAssignmentStore(new Dictionary<string, string>()),
                new JsonBookOutputSnapshotService());

            var snapshot = await service.RefreshAsync(root);

            Assert.NotNull(snapshot.GlobalSettingsIssue);
            var brand = Assert.Single(snapshot.Brands);
            Assert.Equal(BrandValidationStatus.NotValidated, brand.Validation.Status);
            Assert.NotNull(brand.Issue);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AssignmentStorePersistsBookBrandMapping()
    {
        var root = Path.Combine(Path.GetTempPath(), $"word-search-state-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "workspace-state.json");
        try
        {
            var store = new JsonBookBrandAssignmentStore(path);

            await store.SaveAsync("sample-book", "demo");
            var assignments = await store.ReadAsync();

            Assert.Equal("demo", assignments["sample-book"]);
            Assert.Empty(Directory.EnumerateFiles(root, "*.tmp"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static string CopyFixtureToTemporaryRoot()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "TestData", "SingleTopicBook");
        var destination = Path.Combine(Path.GetTempPath(), $"word-search-workspace-{Guid.NewGuid():N}");
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(directory.Replace(source, destination, StringComparison.Ordinal));
        }

        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = file.Replace(source, destination, StringComparison.Ordinal);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        return destination;
    }

    private static BrandValidationService CreateValidationService() =>
        new(new JsonBrandValidationStateStore(), new JsonWordSearchSettingsReader());

    private static CsvBookDataValidationService CreateDataValidationService() =>
        new(new JsonBookDataValidationStateStore());

    private sealed class StubAssignmentStore(IReadOnlyDictionary<string, string> assignments) : IBookBrandAssignmentStore
    {
        public Task<IReadOnlyDictionary<string, string>> ReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(assignments);

        public Task SaveAsync(string bookId, string brandId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
