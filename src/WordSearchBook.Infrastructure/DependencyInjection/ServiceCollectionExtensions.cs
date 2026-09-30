using Microsoft.Extensions.DependencyInjection;
using WordSearchBook.Core.Application;
using WordSearchBook.Core.WordSearch.Application;
using WordSearchBook.Core.WordSearch.Caching;
using WordSearchBook.Core.WordSearch.Generation;
using WordSearchBook.Core.WordSearch.Input;
using WordSearchBook.Core.WordSearch.Rendering;
using WordSearchBook.Core.WordSearch.Settings;
using WordSearchBook.Infrastructure.WordSearch.Caching;
using WordSearchBook.Infrastructure.WordSearch.Input;
using WordSearchBook.Infrastructure.WordSearch.Rendering;
using WordSearchBook.Infrastructure.WordSearch.Settings;

namespace WordSearchBook.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWordSearchBookInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IApplicationInfoProvider, ApplicationInfoProvider>();
        services.AddSingleton<IWordSearchInputReader, CsvWordSearchInputReader>();
        services.AddSingleton<IWordSearchSettingsReader, JsonWordSearchSettingsReader>();
        services.AddSingleton<IWordSearchPuzzleGenerator, WordSearchPuzzleGenerator>();
        services.AddSingleton<IWordSearchBoardRenderer, SystemDrawingWordSearchBoardRenderer>();
        services.AddSingleton<IWordSearchCachePublisher, FileSystemWordSearchCachePublisher>();
        services.AddSingleton<IWordSearchBookGenerationService, WordSearchBookGenerationService>();
        return services;
    }
}
