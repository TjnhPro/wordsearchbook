using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using WordSearchBook.Core.Application.BackgroundTasks;
using WordSearchBook.Desktop.BackgroundTasks;
using WordSearchBook.Desktop.Bridge;
using WordSearchBook.Desktop.Shutdown;
using WordSearchBook.Infrastructure.DependencyInjection;

namespace WordSearchBook.Desktop;

public partial class App : Application
{
    private ServiceProvider? serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        services.AddWordSearchBookInfrastructure();
        services.AddSingleton<IBackgroundTaskManager, BackgroundTaskManager>();
        services.AddSingleton<IApplicationRootProvider, ExecutableApplicationRootProvider>();
        services.AddSingleton<IBrandFolderActionService, BrandFolderActionService>();
        services.AddSingleton<ApplicationCloseCoordinator>();
        services.AddSingleton<WebViewBridgeRouter>();
        services.AddSingleton<MainWindow>();

        serviceProvider = services.BuildServiceProvider();
        serviceProvider.GetRequiredService<MainWindow>().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
