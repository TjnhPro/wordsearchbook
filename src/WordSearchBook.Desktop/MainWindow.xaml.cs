using Microsoft.Web.WebView2.Core;
using System.ComponentModel;
using System.IO;
using System.Windows;
using WordSearchBook.Core.Application.BackgroundTasks;
using WordSearchBook.Desktop.Bridge;

namespace WordSearchBook.Desktop;

public partial class MainWindow : Window
{
    private readonly WebViewBridgeRouter bridgeRouter;
    private readonly IBackgroundTaskManager taskManager;
    private bool allowClose;
    private bool closeFlowRunning;

    public MainWindow(WebViewBridgeRouter bridgeRouter, IBackgroundTaskManager taskManager)
    {
        this.bridgeRouter = bridgeRouter;
        this.taskManager = taskManager;
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
        task.State is BackgroundTaskState.Queued or BackgroundTaskState.Running or BackgroundTaskState.Cancelling);

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
            var active = (await taskManager.ListAsync())
                .Where(task => task.State is BackgroundTaskState.Queued or BackgroundTaskState.Running or BackgroundTaskState.Cancelling)
                .ToArray();
            if (active.Length > 0)
            {
                var answer = MessageBox.Show(
                    "Background work is still active. Cancel it and close Word Search Book?",
                    "Close Word Search Book",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (answer != MessageBoxResult.Yes)
                {
                    return;
                }

                foreach (var task in active)
                {
                    await taskManager.CancelAsync(task.TaskId);
                }

                await Task.WhenAll(active.Select(task =>
                    taskManager.WaitAsync(task.TaskId, TimeSpan.FromSeconds(5)).AsTask()));
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
