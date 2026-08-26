namespace AtomPix.Desktop.Shell;

using System.ComponentModel;
using AtomPix.Desktop.Platform;
using AtomPix.Desktop.ViewModels;
using AtomPix.Desktop.Views;
using AtomUI.Desktop.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

public sealed partial class MainWindow : AtomUI.Desktop.Controls.Window
{
    private bool _closeAuthorized;
    private bool _closePending;
    private readonly Dialog _batchResultDialog;
    private SettingsPageViewModel? _settingsViewModel;
    private ShellViewModel? _shellViewModel;
    private bool _synchronizingBatchResultState;
    private AvaloniaDesktopFeedbackService? _feedback;
    private WindowTitleBar? _titleBar;

    internal WindowTitleBar? ConfiguredTitleBar => _titleBar;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        _batchResultDialog = this.FindControl<Dialog>("BatchResultDialog")
            ?? throw new InvalidOperationException("The batch result dialog is missing from the window template.");
        Opened += HandleOpened;
        Activated += HandleActivated;
        Closing += HandleClosing;
        Closed += HandleClosed;
    }

    public MainWindow(ShellViewModel viewModel, AvaloniaDesktopFeedbackService? feedback = null) : this()
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        DataContext = viewModel;
        _shellViewModel = viewModel;
        _shellViewModel.PropertyChanged += HandleShellPropertyChanged;
        _feedback = feedback;
        _feedback?.Attach(this);
        _settingsViewModel = viewModel.Settings;
        _settingsViewModel.PropertyChanged += HandleSettingsPropertyChanged;

        _batchResultDialog.DataContext = viewModel.BatchResultDetails;
        _batchResultDialog.Content = new BatchResultDialogView
        {
            DataContext = viewModel.BatchResultDetails
        };
        _batchResultDialog.PropertyChanged += HandleBatchResultDialogPropertyChanged;
        _batchResultDialog.IsOpen = viewModel.IsBatchResultOpen;
    }

    protected override void NotifyConfigureTitleBar(WindowTitleBar titleBar)
    {
        base.NotifyConfigureTitleBar(titleBar);
        _titleBar = titleBar;
    }

    private void HandleOpened(object? sender, EventArgs args)
    {
        Opened -= HandleOpened;

        // Let the main window render first, then warm the small settings model while
        // the application is idle. Opening the dialog no longer has to deserialize
        // settings and replace its complete form during the overlay animation.
        Dispatcher.UIThread.Post(PreloadSettingsAsync, DispatcherPriority.Background);
    }

    private async void PreloadSettingsAsync()
    {
        if (_settingsViewModel is null) return;
        await _settingsViewModel.LoadAsync();
    }

    private async void HandleClosing(object? sender, WindowClosingEventArgs args)
    {
        if (_closeAuthorized) return;
        args.Cancel = true;
        if (_closePending || DataContext is not ShellViewModel viewModel) return;

        _closePending = true;
        try
        {
            if (!await viewModel.TryCloseAsync()) return;
            _closeAuthorized = true;
            Close();
        }
        finally
        {
            _closePending = false;
        }
    }

    private void HandleActivated(object? sender, EventArgs args)
    {
        if (DataContext is ShellViewModel viewModel) viewModel.RefreshResultAvailability();
    }

    private void HandleSettingsPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_feedback is null
            || DataContext is not ShellViewModel { IsSettingsOpen: true }
            || sender is not SettingsPageViewModel settings)
        {
            return;
        }

        if (args.PropertyName == nameof(SettingsPageViewModel.SaveMessage)
            && !string.IsNullOrWhiteSpace(settings.SaveMessage))
        {
            _feedback.ShowMessage(
                settings.SaveMessage,
                settings.SaveMessage.StartsWith("设置已保存", StringComparison.Ordinal)
                    ? DesktopFeedbackSeverity.Success
                    : DesktopFeedbackSeverity.Information);
        }
        else if (args.PropertyName == nameof(SettingsPageViewModel.ErrorMessage)
                 && settings.IsReady
                 && !string.IsNullOrWhiteSpace(settings.ErrorMessage))
        {
            _feedback.ShowMessage(settings.ErrorMessage, DesktopFeedbackSeverity.Error);
        }
    }

    private void HandleShellPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(ShellViewModel.IsBatchResultOpen)
            || _shellViewModel is null
            || _synchronizingBatchResultState)
        {
            return;
        }

        _synchronizingBatchResultState = true;
        try
        {
            _batchResultDialog.IsOpen = _shellViewModel.IsBatchResultOpen;
        }
        finally
        {
            _synchronizingBatchResultState = false;
        }
    }

    private void HandleBatchResultDialogPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property != Dialog.IsOpenProperty
            || _shellViewModel is null
            || _synchronizingBatchResultState)
        {
            return;
        }

        _synchronizingBatchResultState = true;
        try
        {
            _shellViewModel.IsBatchResultOpen = _batchResultDialog.IsOpen;
        }
        finally
        {
            _synchronizingBatchResultState = false;
        }
    }

    private void HandleClosed(object? sender, EventArgs args)
    {
        if (_settingsViewModel is not null)
            _settingsViewModel.PropertyChanged -= HandleSettingsPropertyChanged;
        _settingsViewModel = null;
        _batchResultDialog.PropertyChanged -= HandleBatchResultDialogPropertyChanged;
        if (_shellViewModel is not null)
            _shellViewModel.PropertyChanged -= HandleShellPropertyChanged;
        _shellViewModel = null;
        _batchResultDialog.Content = null;
        _feedback = null;
        Opened -= HandleOpened;
        Activated -= HandleActivated;
        Closing -= HandleClosing;
        Closed -= HandleClosed;
        _titleBar = null;
    }
}
