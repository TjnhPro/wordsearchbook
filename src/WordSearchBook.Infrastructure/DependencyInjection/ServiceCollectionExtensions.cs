using Microsoft.Extensions.DependencyInjection;
using WordSearchBook.Core.Application;

namespace WordSearchBook.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWordSearchBookInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IApplicationInfoProvider, ApplicationInfoProvider>();
        return services;
    }
}
