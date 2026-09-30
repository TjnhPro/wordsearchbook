using Microsoft.Extensions.DependencyInjection;
using WordSearchBook.Core.Application;
using WordSearchBook.Core.Application.BackgroundTasks;
using WordSearchBook.Core.Application.Workspace;
using WordSearchBook.Core.WordSearch.Application;
using WordSearchBook.Core.WordSearch.Caching;
using WordSearchBook.Core.WordSearch.Generation;
using WordSearchBook.Core.WordSearch.Input;
using WordSearchBook.Core.WordSearch.Rendering;
using WordSearchBook.Core.WordSearch.Settings;
using WordSearchBook.Core.WordSearch.Validation;
using WordSearchBook.Infrastructure.WordSearch.Caching;
using WordSearchBook.Infrastructure.WordSearch.Input;
using WordSearchBook.Infrastructure.WordSearch.Rendering;
using WordSearchBook.Infrastructure.WordSearch.Settings;
using WordSearchBook.Infrastructure.WordSearch.Validation;
using WordSearchBook.Infrastructure.Workspace;

namespace WordSearchBook.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWordSearchBookInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IApplicationInfoProvider, ApplicationInfoProvider>();
        services.AddSingleton<IWordSearchInputReader, CsvWordSearchInputReader>();
        services.AddSingleton<IWordSearchSettingsReader, JsonWordSearchSettingsReader>();
        services.AddSingleton<IWordSearchSettingsWriter, JsonWordSearchSettingsWriter>();
        services.AddSingleton<IBrandValidationStateStore, JsonBrandValidationStateStore>();
        services.AddSingleton<IBrandValidationService, BrandValidationService>();
        services.AddSingleton<IBookDataValidationStateStore, JsonBookDataValidationStateStore>();
        services.AddSingleton<IBookDataValidationService, CsvBookDataValidationService>();
        services.AddSingleton<IWordSearchPuzzleGenerator, WordSearchPuzzleGenerator>();
        services.AddSingleton<IWordSearchBoardRenderer, SystemDrawingWordSearchBoardRenderer>();
        services.AddSingleton<IWordSearchPageRenderer, SystemDrawingWordSearchPageRenderer>();
        services.AddSingleton<IBrandPagePreviewService, BrandPagePreviewService>();
        services.AddSingleton<IWordSearchCachePublisher, FileSystemWordSearchCachePublisher>();
        services.AddSingleton<IWordSearchBookGenerationService, WordSearchBookGenerationService>();
        services.AddSingleton<IBookBrandAssignmentStore, JsonBookBrandAssignmentStore>();
        services.AddSingleton<IWorkspaceSnapshotService, WordSearchWorkspaceSnapshotService>();
        services.AddKeyedSingleton<IBackgroundTaskWorker, WorkspaceRefreshWorker>(BackgroundTaskKind.WorkspaceRefresh);
        services.AddKeyedSingleton<IBackgroundTaskWorker, BookGenerationWorker>(BackgroundTaskKind.BookGeneration);
        services.AddKeyedSingleton<IBackgroundTaskWorker, BookBrandAssignmentWorker>(BackgroundTaskKind.BookBrandAssignmentSave);
        services.AddKeyedSingleton<IBackgroundTaskWorker, SettingsSaveWorker>(BackgroundTaskKind.SettingsSave);
        services.AddKeyedSingleton<IBackgroundTaskWorker, BrandCreateWorker>(BackgroundTaskKind.BrandCreate);
        services.AddKeyedSingleton<IBackgroundTaskWorker, BrandValidationWorker>(BackgroundTaskKind.BrandValidation);
        services.AddKeyedSingleton<IBackgroundTaskWorker, BrandPagePreviewWorker>(BackgroundTaskKind.BrandPagePreview);
        return services;
    }
}
