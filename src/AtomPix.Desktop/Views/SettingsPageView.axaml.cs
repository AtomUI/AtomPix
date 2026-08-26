namespace AtomPix.Desktop.Views;

using AtomPix.Desktop.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

public sealed partial class SettingsPageView : UserControl
{
    private CancellationTokenSource? _scrollCancellation;
    private SettingsPageViewModel? _viewModel;
    private bool _isAnimatingScroll;

    public SettingsPageView()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += HandleDataContextChanged;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AttachViewModel(DataContext as SettingsPageViewModel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelScrollAnimation();
        AttachViewModel(null);
        base.OnDetachedFromVisualTree(e);
    }

    private void HandleDataContextChanged(object? sender, EventArgs args) =>
        AttachViewModel(DataContext as SettingsPageViewModel);

    private void AttachViewModel(SettingsPageViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel)) return;
        if (_viewModel is not null)
            _viewModel.SectionNavigationRequested -= HandleSectionNavigationRequested;

        _viewModel = viewModel;
        if (_viewModel is not null)
            _viewModel.SectionNavigationRequested += HandleSectionNavigationRequested;
    }

    private void HandleSectionNavigationRequested(SettingsSection section) =>
        _ = ScrollToSectionAsync(section);

    private async Task ScrollToSectionAsync(SettingsSection section)
    {
        var scrollViewer = this.FindControl<AtomUI.Desktop.Controls.ScrollViewer>("SettingsScrollViewer");
        var sections = this.FindControl<StackPanel>("SettingsSections");
        var target = this.FindControl<StackPanel>(GetSectionControlName(section));
        if (scrollViewer is null || sections is null || target is null) return;

        var point = target.TranslatePoint(default, sections);
        if (point is null) return;

        CancelScrollAnimation();
        var cancellation = new CancellationTokenSource();
        _scrollCancellation = cancellation;
        _isAnimatingScroll = true;

        var start = scrollViewer.Offset.Y;
        var maximum = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        var destination = Math.Clamp(point.Value.Y, 0, maximum);
        const int durationMilliseconds = 220;
        var started = Environment.TickCount64;

        try
        {
            while (true)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var elapsed = Environment.TickCount64 - started;
                var progress = Math.Clamp(elapsed / (double)durationMilliseconds, 0, 1);
                var eased = 1 - Math.Pow(1 - progress, 3);
                scrollViewer.Offset = new Vector(scrollViewer.Offset.X, start + ((destination - start) * eased));
                if (progress >= 1) break;
                await Task.Delay(16, cancellation.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // A newer navigation request or view detach superseded this animation.
        }
        finally
        {
            if (ReferenceEquals(_scrollCancellation, cancellation))
            {
                _scrollCancellation = null;
                _isAnimatingScroll = false;
            }
            cancellation.Dispose();
        }
    }

    private void SettingsScrollChanged(object? sender, ScrollChangedEventArgs args)
    {
        if (_isAnimatingScroll || _viewModel is null
            || sender is not AtomUI.Desktop.Controls.ScrollViewer scrollViewer)
            return;

        var sections = this.FindControl<StackPanel>("SettingsSections");
        if (sections is null) return;

        var maximum = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        if (maximum > 0 && scrollViewer.Offset.Y >= maximum - 1)
        {
            _viewModel.SelectedSection = SettingsSection.About;
            return;
        }

        var probe = scrollViewer.Offset.Y + Math.Min(120, scrollViewer.Viewport.Height * 0.25);
        var selected = SettingsSection.Compression;
        foreach (var section in Enum.GetValues<SettingsSection>())
        {
            var target = this.FindControl<StackPanel>(GetSectionControlName(section));
            var point = target?.TranslatePoint(default, sections);
            if (point is not null && point.Value.Y <= probe)
                selected = section;
        }

        _viewModel.SelectedSection = selected;
    }

    private static string GetSectionControlName(SettingsSection section) => section switch
    {
        SettingsSection.Compression => "CompressionSettingsSection",
        SettingsSection.Conversion => "ConversionSettingsSection",
        SettingsSection.Output => "OutputSettingsSection",
        SettingsSection.About => "AboutSettingsSection",
        _ => throw new ArgumentOutOfRangeException(nameof(section), section, null)
    };

    private void CancelScrollAnimation()
    {
        var cancellation = Interlocked.Exchange(ref _scrollCancellation, null);
        cancellation?.Cancel();
        _isAnimatingScroll = false;
    }
}
