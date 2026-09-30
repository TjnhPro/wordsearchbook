using Microsoft.Web.WebView2.Core;
using System.ComponentModel;
using System.IO;
using System.Windows;
using WordSearchBook.Core.Application.BackgroundTasks;
using WordSearchBook.Desktop.Bridge;
using WordSearchBook.Desktop.Shutdown;

namespace WordSearchBook.Desktop;

public partial class MainWindow : Window
{
    private readonly WebViewBridgeRouter bridgeRouter;
    private readonly ApplicationCloseCoordinator closeCoordinator;
    private bool allowClose;
    private bool closeFlowRunning;

    public MainWindow(
        WebViewBridgeRouter bridgeRouter,
        ApplicationCloseCoordinator closeCoordinator)
    {
        this.bridgeRouter = bridgeRouter;
        this.closeCoordinator = closeCoordinator;
        InitializeComponent();
        Closing += OnClosing;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var pagePath = FrontendPathResolver.GetIndexPath(AppContext.BaseDirectory);
            if (!File.Exists(pagePath))
            {
                throw new FileNotFoundException("The local frontend entry point was not found.", pagePath);
            }

            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: GetWebViewUserDataFolder());

            await Browser.EnsureCoreWebView2Async(environment);
            Browser.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            Browser.CoreWebView2.Navigate(new Uri(pagePath).AbsoluteUri);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"The application UI could not start. Ensure Microsoft Edge WebView2 Runtime is installed and the frontend assets are present.\n\n{exception.Message}",
                "Startup failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string? message;
        try
        {
            message = e.TryGetWebMessageAsString();
        }
        catch (ArgumentException)
        {
            message = null;
        }

        Browser.CoreWebView2.PostWebMessageAsJson(await bridgeRouter.HandleAsync(message));
    }

    internal static string GetWebViewUserDataFolder() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WordSearchBook",
            "WebView2");

    internal static bool HasActiveTasks(IEnumerable<BackgroundTaskSnapshot> tasks) => tasks.Any(task =>
        ApplicationCloseCoordinator.IsActive(task.State));

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (allowClose)
        {
            return;
        }

        e.Cancel = true;
        if (closeFlowRunning)
        {
            return;
        }

        closeFlowRunning = true;
        try
        {
            var active = await closeCoordinator.GetActiveTasksAsync();
            var dialog = new CloseApplicationDialog(
                active.Count,
                () => closeCoordinator.CancelAndWaitAsync(active, TimeSpan.FromSeconds(5)))
            {
                Owner = this
            };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            allowClose = true;
            Close();
        }
        finally
        {
            closeFlowRunning = false;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (Browser.CoreWebView2 is not null)
        {
            Browser.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
        }
    }
}
