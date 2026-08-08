namespace AtomPix.Desktop.Platform;

using AtomPix.Core.Settings;
using AtomUI.Desktop.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Styling;
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

        var result = await MessageBox.ShowMessageBoxModalAsync(
            BuildMessage(message),
            options: new MessageBoxOptions
            {
                Title = title,
                Style = MessageBoxStyle.Confirm,
                OkButtonText = confirmText,
                CancelButtonText = cancelText,
                MinWidth = 420,
                MaxWidth = 560
            },
            topLevel: topLevel,
            cancellationToken: cancellationToken);
        return Equals(result, DialogCode.Accepted);
    }

    public async Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken)
    {
        var topLevel = ResolveTopLevel();
        if (topLevel is null)
        {
            return;
        }

        await MessageBox.ShowMessageBoxModalAsync(
            BuildMessage(message),
            options: new MessageBoxOptions
            {
                Title = title,
                Style = MessageBoxStyle.Error,
                OkButtonText = "知道了",
                MinWidth = 420,
                MaxWidth = 560
            },
            topLevel: topLevel,
            cancellationToken: cancellationToken);
    }

    public async Task ShowInformationAsync(string title, string message, CancellationToken cancellationToken)
    {
        var topLevel = ResolveTopLevel();
        if (topLevel is null) return;

        await MessageBox.ShowMessageBoxModalAsync(
            BuildMessage(message),
            options: new MessageBoxOptions
            {
                Title = title,
                Style = MessageBoxStyle.Information,
                OkButtonText = "知道了",
                MinWidth = 420,
                MaxWidth = 560
            },
            topLevel: topLevel,
            cancellationToken: cancellationToken);
    }

    public async Task<UnsavedChangesChoice> ChooseUnsavedChangesAsync(CancellationToken cancellationToken)
    {
        var topLevel = ResolveTopLevel();
        if (topLevel is null) return UnsavedChangesChoice.Stay;

        var choice = UnsavedChangesChoice.Stay;
        var content = BuildMessage("设置包含尚未保存的修改。可以先保存、放弃本次修改，或留在设置页继续编辑。");
        await Dialog.ShowDialogModalAsync(
            content,
            options: new DialogOptions
            {
                Title = "保存设置修改？",
                StandardButtons = DialogStandardButton.Save | DialogStandardButton.No | DialogStandardButton.Cancel,
                DefaultStandardButton = DialogStandardButton.Save,
                HostMinWidth = 460,
                HostMaxWidth = 560,
                BeforeCloseAsync = context =>
                {
                    choice = context.SourceButton?.StandardButtonType switch
                    {
                        DialogStandardButton.Save => UnsavedChangesChoice.Save,
                        DialogStandardButton.No => UnsavedChangesChoice.Discard,
                        _ => UnsavedChangesChoice.Stay
                    };
                    return ValueTask.FromResult(true);
                }
            },
            topLevel: topLevel,
            cancellationToken: cancellationToken);
        return choice;
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

public sealed class AvaloniaDesktopAppearanceService : IDesktopAppearanceService
{
    public void Apply(ThemeMode themeMode)
    {
        if (Application.Current is null)
        {
            return;
        }

        Application.Current.RequestedThemeVariant = themeMode switch
        {
            ThemeMode.Light => ThemeVariant.Light,
            ThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
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
