using Microsoft.Web.WebView2.Core;
using System.IO;
using System.Windows;
using WordSearchBook.Desktop.Bridge;

namespace WordSearchBook.Desktop;

public partial class MainWindow : Window
{
    private readonly WebViewBridgeRouter bridgeRouter;

    public MainWindow(WebViewBridgeRouter bridgeRouter)
    {
        this.bridgeRouter = bridgeRouter;
        InitializeComponent();
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
}
