using Microsoft.Web.WebView2.Core;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using WordSearchBook.Core.Application.BackgroundTasks;
using WordSearchBook.Desktop.Bridge;
using WordSearchBook.Desktop.Shutdown;

namespace WordSearchBook.Desktop;

public partial class MainWindow : Window
{
    private readonly WebViewBridgeRouter bridgeRouter;
    private readonly ApplicationCloseCoordinator closeCoordinator;
    private readonly FrontendResourceProvider frontendResources = new();
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
            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: GetWebViewUserDataFolder());

            await Browser.EnsureCoreWebView2Async(environment);
            Browser.CoreWebView2.AddWebResourceRequestedFilter(
                $"{FrontendResourceProvider.ApplicationOrigin}*",
                CoreWebView2WebResourceContext.All);
            Browser.CoreWebView2.WebResourceRequested += OnWebResourceRequested;
            Browser.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            Browser.CoreWebView2.Navigate(FrontendResourceProvider.IndexUri.AbsoluteUri);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"The application UI could not start. Ensure Microsoft Edge WebView2 Runtime is installed.\n\n{exception.Message}",
                "Startup failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var resource = frontendResources.Open(e.Request.Uri);
        if (resource is null)
        {
            e.Response = Browser.CoreWebView2.Environment.CreateWebResourceResponse(
                new MemoryStream("Not Found"u8.ToArray()),
                404,
                "Not Found",
                "Content-Type: text/plain; charset=utf-8\r\nCache-Control: no-store");
            return;
        }

        e.Response = Browser.CoreWebView2.Environment.CreateWebResourceResponse(
            resource.Content,
            200,
            "OK",
            $"Content-Type: {resource.ContentType}\r\nCache-Control: no-store");
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

    private void OnClosing(object? sender, CancelEventArgs e)
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
        _ = Dispatcher.BeginInvoke(RunCloseFlowAsync, DispatcherPriority.Normal);
    }

    private async void RunCloseFlowAsync()
    {
        try
        {
            var active = await closeCoordinator.GetActiveTasksAsync();
            if (active.Count == 0)
            {
                CloseApplication();
                return;
            }

            var dialog = new CloseApplicationDialog(
                active.Count,
                () => closeCoordinator.CancelAndWaitAsync(active, TimeSpan.FromSeconds(5)))
            {
                Owner = this
            };
            dialog.ShutdownReady += OnShutdownReady;
            dialog.ShowDialog();
            dialog.ShutdownReady -= OnShutdownReady;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Application close flow failed: {exception}");
            CloseApplication();
        }
        finally
        {
            closeFlowRunning = false;
        }
    }

    private void OnShutdownReady(object? sender, EventArgs e) => CloseApplication();

    private void CloseApplication()
    {
        if (allowClose)
        {
            return;
        }

        allowClose = true;
        Close();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (Browser.CoreWebView2 is not null)
        {
            Browser.CoreWebView2.WebResourceRequested -= OnWebResourceRequested;
            Browser.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
        }
    }
}
