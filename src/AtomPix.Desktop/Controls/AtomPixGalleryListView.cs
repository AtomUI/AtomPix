namespace AtomPix.Desktop.Controls;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AtomUI.Desktop.Controls;
using AtomScrollViewer = AtomUI.Desktop.Controls.ScrollViewer;

/// <summary>
/// AtomUI ListView specialization for the zero-gap thumbnail strip. The public
/// ListView preparation hook is used so recycled containers keep identical geometry.
/// </summary>
public sealed class AtomPixGalleryListView : ListView
{
    public const double ThumbnailWidth = 84;
    public const double ThumbnailHeight = 56;
    private const double WheelScrollStep = ThumbnailWidth;
    private AtomScrollViewer? _scrollViewer;
    private CancellationTokenSource? _scrollAnimation;
    private CancellationTokenSource? _resumeFollow;
    private DateTimeOffset _automaticFollowSuspendedUntil;

    public AtomPixGalleryListView()
    {
        AddHandler(
            PointerWheelChangedEvent,
            OnGalleryPointerWheelChanged,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _scrollViewer = e.NameScope.Find<AtomScrollViewer>("PART_ScrollViewer");
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _scrollAnimation?.Cancel();
        _scrollAnimation?.Dispose();
        _scrollAnimation = null;
        _resumeFollow?.Cancel();
        _resumeFollow?.Dispose();
        _resumeFollow = null;
        _scrollViewer = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void PrepareListViewItem(ListViewItem listItem, object? item, int index)
    {
        base.PrepareListViewItem(listItem, item, index);
        listItem.Width = ThumbnailWidth;
        listItem.Height = ThumbnailHeight;
        listItem.MinHeight = ThumbnailHeight;
        listItem.Margin = new Thickness(0);
        listItem.Padding = new Thickness(0);
        listItem.CornerRadius = new CornerRadius(0);
        listItem.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        listItem.VerticalContentAlignment = VerticalAlignment.Stretch;
    }

    private void OnGalleryPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_scrollViewer is null)
        {
            return;
        }

        var wheelDelta = Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y)
            ? e.Delta.X
            : e.Delta.Y;
        if (Math.Abs(wheelDelta) < double.Epsilon)
        {
            return;
        }

        var maxOffset = Math.Max(0, _scrollViewer.Extent.Width - _scrollViewer.Viewport.Width);
        var nextOffset = Math.Clamp(
            _scrollViewer.Offset.X - wheelDelta * WheelScrollStep,
            0,
            maxOffset);

        if (Math.Abs(nextOffset - _scrollViewer.Offset.X) < double.Epsilon)
        {
            return;
        }

        _scrollViewer.Offset = new Vector(nextOffset, _scrollViewer.Offset.Y);
        _automaticFollowSuspendedUntil = DateTimeOffset.UtcNow.AddMilliseconds(1200);
        _resumeFollow?.Cancel();
        _scrollAnimation?.Cancel();
        e.Handled = true;
    }

    public void ScrollIndexIntoView(int index, bool animate, bool respectUserSuspension)
    {
        if (_scrollViewer is null || index < 0) return;
        if (respectUserSuspension && DateTimeOffset.UtcNow < _automaticFollowSuspendedUntil)
        {
            ScheduleResumeFollow(index, animate);
            return;
        }

        var left = index * ThumbnailWidth;
        var right = left + ThumbnailWidth;
        var current = _scrollViewer.Offset.X;
        var target = current;
        if (left < current) target = left;
        else if (right > current + _scrollViewer.Viewport.Width) target = right - _scrollViewer.Viewport.Width;

        var maxOffset = Math.Max(0, _scrollViewer.Extent.Width - _scrollViewer.Viewport.Width);
        target = Math.Clamp(target, 0, maxOffset);
        if (Math.Abs(target - current) < 0.5) return;
        if (!animate)
        {
            _scrollViewer.Offset = new Vector(target, _scrollViewer.Offset.Y);
            return;
        }

        _scrollAnimation?.Cancel();
        _scrollAnimation?.Dispose();
        _scrollAnimation = new CancellationTokenSource();
        var duration = Math.Min(280, 200 + (Math.Max(0, Math.Abs(target - current) - ThumbnailWidth) / ThumbnailWidth * 20));
        _ = AnimateOffsetAsync(current, target, duration, _scrollAnimation.Token);
    }

    private void ScheduleResumeFollow(int index, bool animate)
    {
        _resumeFollow?.Cancel();
        _resumeFollow?.Dispose();
        _resumeFollow = new CancellationTokenSource();
        var cancellationToken = _resumeFollow.Token;
        var delay = _automaticFollowSuspendedUntil - DateTimeOffset.UtcNow;
        _ = ResumeFollowAsync(index, animate, delay < TimeSpan.Zero ? TimeSpan.Zero : delay, cancellationToken);
    }

    private async Task ResumeFollowAsync(int index, bool animate, TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
            await Dispatcher.UIThread.InvokeAsync(() => ScrollIndexIntoView(index, animate, respectUserSuspension: false));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task AnimateOffsetAsync(double from, double to, double durationMs, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var progress = Math.Clamp((DateTimeOffset.UtcNow - started).TotalMilliseconds / durationMs, 0, 1);
                var eased = 1 - Math.Pow(1 - progress, 3);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_scrollViewer is not null)
                    {
                        _scrollViewer.Offset = new Vector(from + ((to - from) * eased), _scrollViewer.Offset.Y);
                    }
                });
                if (progress >= 1) break;
                await Task.Delay(16, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
