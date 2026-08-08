namespace AtomPix.Desktop.Controls;

using AtomPix.Desktop.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

public sealed partial class BrowserThumbnailView : UserControl
{
    private bool _isAttached;

    public BrowserThumbnailView() => AvaloniaXamlLoader.Load(this);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        RequestThumbnail();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_isAttached)
        {
            RequestThumbnail();
        }
    }

    private void RequestThumbnail()
    {
        if (DataContext is BrowserItemViewModel item && item.EnsureThumbnailCommand.CanExecute(null))
        {
            item.EnsureThumbnailCommand.Execute(null);
        }
    }
}
