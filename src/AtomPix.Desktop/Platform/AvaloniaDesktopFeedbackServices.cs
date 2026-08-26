namespace AtomPix.Desktop.Platform;

using AtomUI.Desktop.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Threading;

public sealed class AvaloniaDesktopDialogService : IDesktopDialogService
{
    public async Task<bool> ConfirmAsync(
        string title,
        string message,
        string confirmText,
        string cancelText,
        CancellationToken cancellationToken)
    {
        var topLevel = ResolveTopLevel();
        if (topLevel is null)
        {
            return false;
        }

        var result = await MessageBox.ShowMessageBoxModalAsync<DesktopMessageTextView, string>(
            message,
            options: new MessageBoxOptions
            {
                Title = title,
                Style = MessageBoxStyle.Confirm,
                MinWidth = 420,
                MaxWidth = 560
            },
            topLevel: topLevel);
        return Equals(result, DialogCode.Accepted);
    }

    public async Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken)
    {
        var topLevel = ResolveTopLevel();
        if (topLevel is null)
        {
            return;
        }

        await MessageBox.ShowMessageBoxModalAsync<DesktopMessageTextView, string>(
            message,
            options: new MessageBoxOptions
            {
                Title = title,
                Style = MessageBoxStyle.Error,
                MinWidth = 420,
                MaxWidth = 560
            },
            topLevel: topLevel);
    }

    public async Task ShowInformationAsync(string title, string message, CancellationToken cancellationToken)
    {
        var topLevel = ResolveTopLevel();
        if (topLevel is null) return;

        await MessageBox.ShowMessageBoxModalAsync<DesktopMessageTextView, string>(
            message,
            options: new MessageBoxOptions
            {
                Title = title,
                Style = MessageBoxStyle.Information,
                MinWidth = 420,
                MaxWidth = 560
            },
            topLevel: topLevel);
    }

    private static Avalonia.Controls.SelectableTextBlock BuildMessage(string message) => new()
    {
        Text = message,
        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        MaxWidth = 480
    };

    private static TopLevel? ResolveTopLevel() =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}

public sealed class DesktopMessageTextView : Avalonia.Controls.SelectableTextBlock
{
    public DesktopMessageTextView()
    {
        TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        MaxWidth = 480;
        DataContextChanged += (_, _) =>
        {
            // AtomUI's overlay may later inherit the host Window DataContext.
            // Only the explicit message payload is allowed to replace the text.
            if (DataContext is string message)
            {
                Text = message;
            }
        };
    }
}

public sealed class AvaloniaDesktopClipboardService : IDesktopClipboardService
{
    public async Task<bool> SetTextAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var topLevel = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (topLevel?.Clipboard is null)
        {
            return false;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await topLevel.Clipboard.SetTextAsync(text);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }
}

public sealed class AvaloniaDesktopDispatcher : IDesktopDispatcher
{
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }
}

public sealed class AvaloniaDesktopFeedbackService : IDesktopFeedbackService, IDisposable
{
    private WindowMessageManager? _messages;
    private WindowNotificationManager? _notifications;

    public void Attach(TopLevel host)
    {
        ArgumentNullException.ThrowIfNull(host);
        DisposeManagers();
        _messages = new WindowMessageManager(host)
        {
            Position = NotificationPosition.TopCenter,
            MaxItems = 3
        };
        _notifications = new WindowNotificationManager(host)
        {
            Position = NotificationPosition.TopRight,
            MaxItems = 3
        };
    }

    public void ShowMessage(
        string message,
        DesktopFeedbackSeverity severity = DesktopFeedbackSeverity.Information,
        TimeSpan? expiration = null)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        Dispatcher.UIThread.Post(() => _messages?.Show(new AtomUI.Desktop.Controls.Message(
            message,
            severity switch
            {
                DesktopFeedbackSeverity.Success => MessageType.Success,
                DesktopFeedbackSeverity.Warning => MessageType.Warning,
                DesktopFeedbackSeverity.Error => MessageType.Error,
                _ => MessageType.Information
            },
            expiration: expiration ?? TimeSpan.FromSeconds(5))));
    }

    public void ShowNotification(DesktopNotificationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Dispatcher.UIThread.Post(() => _notifications?.Show(new Notification(
            request.Title,
            request.Content,
            request.Severity switch
            {
                DesktopFeedbackSeverity.Success => NotificationType.Success,
                DesktopFeedbackSeverity.Warning => NotificationType.Warning,
                DesktopFeedbackSeverity.Error => NotificationType.Error,
                DesktopFeedbackSeverity.Information => NotificationType.Information,
                _ => NotificationType.Information
            },
            expiration: request.Expiration,
            onClick: request.OnClick)));
    }

    public void Dispose() => DisposeManagers();

    private void DisposeManagers()
    {
        _messages?.Dispose();
        _messages = null;
        _notifications?.Dispose();
        _notifications = null;
    }
}
