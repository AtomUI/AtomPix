namespace AtomPix.Desktop.Views;

using System.ComponentModel;
using AtomPix.Desktop.Controls;
using AtomPix.Desktop.ViewModels;
using AtomUI.Labs.Controls.ImageGallery;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

public sealed partial class ImageBrowserView : UserControl
{
    private ImageGallery? _gallery;
    private CropCanvas? _cropCanvas;
    private ImageBrowserViewModel? _viewModel;
    private CropEditorViewModel? _cropEditor;
    private ImageGalleryImageLease? _cropImageLease;
    private ImageGalleryItemAdapter? _cropImageLeaseItem;
    private bool? _appliedCropMode;
    private bool _suppressGallerySelectionProjection;

    public ImageBrowserView()
    {
        AvaloniaXamlLoader.Load(this);
        _gallery = this.FindControl<ImageGallery>("ImageGalleryViewer");
        _cropCanvas = this.FindControl<CropCanvas>("BrowserCropCanvas");
        if (_gallery is not null)
        {
            _gallery.SelectionChanged += HandleGallerySelectionChanged;
        }
        if (_cropCanvas is not null)
        {
            _cropCanvas.SelectionChanged += HandleCropSelectionChanged;
            _cropCanvas.SizeChanged += HandleCropCanvasSizeChanged;
        }

        DataContextChanged += HandleDataContextChanged;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_gallery is not null)
        {
            _gallery.CurrentImageResourceChanged += HandleCurrentImageResourceChanged;
        }
        AttachViewModel(DataContext as ImageBrowserViewModel);
        ApplyGalleryMode();
        RefreshCropImageLease();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_gallery is not null)
        {
            _gallery.CurrentImageResourceChanged -= HandleCurrentImageResourceChanged;
            _gallery.MainImageDecodeSizeHint = null;
        }
        AttachViewModel(null);
        ReplaceCropImageLease(null);
        base.OnDetachedFromVisualTree(e);
    }

    private void HandleDataContextChanged(object? sender, EventArgs e)
    {
        AttachViewModel(DataContext as ImageBrowserViewModel);
        ApplyGalleryMode();
        RefreshCropImageLease();
    }

    private void AttachViewModel(ImageBrowserViewModel? value)
    {
        if (ReferenceEquals(_viewModel, value)) return;
        if (_viewModel is not null) _viewModel.PropertyChanged -= HandleViewModelPropertyChanged;
        AttachCropEditor(null);
        _viewModel = value;
        _appliedCropMode = null;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += HandleViewModelPropertyChanged;
            AttachCropEditor(_viewModel.CropEditor);
        }
    }

    private void AttachCropEditor(CropEditorViewModel? value)
    {
        if (ReferenceEquals(_cropEditor, value)) return;
        if (_cropEditor is not null) _cropEditor.PropertyChanged -= HandleCropEditorPropertyChanged;
        _cropEditor = value;
        if (_cropEditor is not null) _cropEditor.PropertyChanged += HandleCropEditorPropertyChanged;
    }

    private void HandleViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ImageBrowserViewModel.CropEditor))
        {
            AttachCropEditor(_viewModel?.CropEditor);
        }
        if (e.PropertyName is nameof(ImageBrowserViewModel.IsCropMode)
            or nameof(ImageBrowserViewModel.CropEditor)
            or nameof(ImageBrowserViewModel.CurrentItem)
            or nameof(ImageBrowserViewModel.State)
            or null or "")
        {
            ApplyGalleryMode();
            RefreshCropImageLease();
        }
    }

    private void HandleCropEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CropEditorViewModel.InputPath)
            or nameof(CropEditorViewModel.ContentState)
            or null or "")
        {
            RefreshCropImageLease();
        }
    }

    private void HandleCurrentImageResourceChanged(object? sender, EventArgs e) =>
        RefreshCropImageLease();

    private void HandleGallerySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressGallerySelectionProjection
            || _gallery?.SelectedItem is not ImageGalleryItemAdapter selected
            || _viewModel is null
            || ReferenceEquals(_viewModel.SelectedGalleryItem, selected))
        {
            return;
        }

        _viewModel.SelectedGalleryItem = selected;
    }

    private void HandleCropCanvasSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateDecodeSizeHint();
        if (_viewModel?.IsCropMode == true) RefreshCropImageLease();
    }

    private void ApplyGalleryMode()
    {
        if (_gallery is null) return;
        var crop = _viewModel?.IsCropMode == true;
        var enteringCrop = crop && _appliedCropMode != true;
        _gallery.MainImageMode = crop
            ? ImageGalleryMainImageMode.ResourceOnly
            : ImageGalleryMainImageMode.Presented;
        if (enteringCrop
            && _viewModel?.SelectedGalleryItem is { } expected
            && !ReferenceEquals(_gallery.SelectedItem, expected))
        {
            // Browser CurrentItem is authoritative. ImageGallery can still have a null
            // SelectedItem during the first folder-load layout pass; ResourceOnly then
            // cannot issue a matching external lease until the user clicks a thumbnail.
            // Reconcile exactly once when entering Crop, without interfering with later
            // filmstrip-driven selection changes.
            _gallery.SelectedItem = expected;
        }
        if (enteringCrop && _viewModel?.SelectedGalleryItem is { } selected)
        {
            if (_gallery.TryAcquireCurrentImage(selected, out var readyLease))
            {
                readyLease!.Dispose();
            }
            else
            {
                RecommitGallerySelection(selected);
            }
        }

        _appliedCropMode = crop;
        UpdateDecodeSizeHint();
        if (!crop) ReplaceCropImageLease(null);
    }

    private void RecommitGallerySelection(ImageGalleryItemAdapter selected)
    {
        if (_gallery is null || _viewModel is null || _viewModel.GalleryItems.Count < 2) return;

        var selectedIndex = -1;
        for (var index = 0; index < _viewModel.GalleryItems.Count; index++)
        {
            if (ReferenceEquals(_viewModel.GalleryItems[index], selected))
            {
                selectedIndex = index;
                break;
            }
        }
        if (selectedIndex < 0) return;

        // ImageGallery 6.0.8 can retain a Ready slot tied to the descriptor set that
        // preceded a dynamically attached multi-item ItemsSource. Re-commit through
        // a different valid index and back after the collection is materialized. The
        // one-way VM binding plus this guard prevents this internal handshake from
        // changing AtomPix's CurrentItem or probing the temporary item.
        _suppressGallerySelectionProjection = true;
        try
        {
            _gallery.SelectedIndex = selectedIndex == 0 ? 1 : 0;
            _gallery.SelectedIndex = selectedIndex;
        }
        finally
        {
            _suppressGallerySelectionProjection = false;
        }
    }

    private void UpdateDecodeSizeHint()
    {
        if (_gallery is null || _cropCanvas is null || _viewModel?.IsCropMode != true)
        {
            if (_gallery is not null) _gallery.MainImageDecodeSizeHint = null;
            return;
        }

        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1d;
        var width = Math.Max(1, (int)Math.Ceiling(_cropCanvas.Bounds.Width * scale));
        var height = Math.Max(1, (int)Math.Ceiling(_cropCanvas.Bounds.Height * scale));
        _gallery.MainImageDecodeSizeHint = new PixelSize(width, height);
    }

    private void RefreshCropImageLease()
    {
        if (_gallery is null || _cropCanvas is null || _viewModel?.IsCropMode != true)
        {
            ReplaceCropImageLease(null);
            return;
        }

        if (_gallery.SelectedItem is not ImageGalleryItemAdapter expected
            || _cropEditor is null
            || string.IsNullOrWhiteSpace(_cropEditor.InputPath)
            || !string.Equals(
                Path.GetFullPath(expected.Item.Path.Value),
                Path.GetFullPath(_cropEditor.InputPath),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            ReplaceCropImageLease(null);
            return;
        }

        if (_gallery.TryAcquireCurrentImage(expected, out var lease))
        {
            ReplaceCropImageLease(lease, expected);
            return;
        }

        // A decode-size change can temporarily make TryAcquireCurrentImage return
        // false while ImageGallery upgrades the same selected image. The lease is
        // independently owned by this view, so retain it until the replacement is
        // ready. Clearing it here produces a blank Crop canvas that only a later
        // thumbnail click can recover.
        if (_cropImageLease is not null && ReferenceEquals(_cropImageLeaseItem, expected))
        {
            return;
        }

        ReplaceCropImageLease(null);
    }

    private void ReplaceCropImageLease(
        ImageGalleryImageLease? value,
        ImageGalleryItemAdapter? item = null)
    {
        var previous = _cropImageLease;
        _cropImageLease = value;
        _cropImageLeaseItem = value is null ? null : item;
        if (_cropCanvas is not null)
        {
            _cropCanvas.ImageSource = value?.Image;
        }
        previous?.Dispose();
    }

    private void HandleCropSelectionChanged(object? sender, CropCanvasSelection selection)
    {
        _cropEditor?.ApplyCanvasSelection(selection);
    }
}
