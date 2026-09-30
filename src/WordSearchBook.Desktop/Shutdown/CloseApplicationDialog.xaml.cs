using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace WordSearchBook.Desktop.Shutdown;

public partial class CloseApplicationDialog : Window
{
    private static readonly TimeSpan MinimumClosingStateDuration = TimeSpan.FromMilliseconds(500);
    private readonly Func<Task> closeAsync;
    private bool closing;
    private bool closeCompleted;

    internal CloseApplicationDialog(int activeTaskCount, Func<Task> closeAsync)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(activeTaskCount);
        ArgumentNullException.ThrowIfNull(closeAsync);

        this.closeAsync = closeAsync;
        InitializeComponent();
        if (activeTaskCount == 0)
        {
            DialogTitle.Text = "Close Word Search Book?";
            ConfirmationMessage.Text = "The application is ready to close.";
        }
        else
        {
            ConfirmationMessage.Text = activeTaskCount == 1
                ? "1 background task is still running. Closing will cancel it before Word Search Book exits."
                : $"{activeTaskCount} background tasks are still running. Closing will cancel them before Word Search Book exits.";
        }
    }

    internal bool IsClosing => closing;

    private void OnKeepOpenClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private async void OnCloseAppClick(object sender, RoutedEventArgs e)
    {
        if (closing)
        {
            return;
        }

        closing = true;
        DialogEyebrow.Text = "CLOSING APPLICATION";
        DialogTitle.Text = "Closing Word Search Book";
        ConfirmationMessage.Visibility = Visibility.Collapsed;
        ConfirmationActions.Visibility = Visibility.Collapsed;
        ClosingState.Visibility = Visibility.Visible;
        await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.Render);

        try
        {
            await Task.WhenAll(closeAsync(), Task.Delay(MinimumClosingStateDuration));
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Application close coordination failed: {exception}");
        }
        finally
        {
            closeCompleted = true;
            DialogResult = true;
        }
    }

    private void OnDialogClosing(object? sender, CancelEventArgs e)
    {
        if (closing && !closeCompleted)
        {
            e.Cancel = true;
        }
    }
}
