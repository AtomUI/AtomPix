namespace AtomPix.Desktop.Controls;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

public sealed class AtomPixImageViewport : UserControl
{
    public static readonly StyledProperty<byte[]?> PreviewBytesProperty =
        AvaloniaProperty.Register<AtomPixImageViewport, byte[]?>(nameof(PreviewBytes));

    public static readonly StyledProperty<bool> FitToViewportProperty =
        AvaloniaProperty.Register<AtomPixImageViewport, bool>(nameof(FitToViewport), true);

    public static readonly StyledProperty<double> ZoomScaleProperty =
        AvaloniaProperty.Register<AtomPixImageViewport, double>(nameof(ZoomScale), 1d);

    public static readonly StyledProperty<string?> BackgroundHexProperty =
        AvaloniaProperty.Register<AtomPixImageViewport, string?>(nameof(BackgroundHex));

    private readonly Border _imageHost;
    private readonly Image _image;
    private Bitmap? _bitmap;

    public AtomPixImageViewport()
    {
        _image = new Image
        {
            Stretch = Stretch.Fill,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        _imageHost = new Border
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
            Child = _image
        };
        Content = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _imageHost
        };
    }

    public byte[]? PreviewBytes
    {
        get => GetValue(PreviewBytesProperty);
        set => SetValue(PreviewBytesProperty, value);
    }

    public bool FitToViewport
    {
        get => GetValue(FitToViewportProperty);
        set => SetValue(FitToViewportProperty, value);
    }

    public double ZoomScale
    {
        get => GetValue(ZoomScaleProperty);
        set => SetValue(ZoomScaleProperty, value);
    }

    public string? BackgroundHex
    {
        get => GetValue(BackgroundHexProperty);
        set => SetValue(BackgroundHexProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PreviewBytesProperty)
        {
            ReplaceBitmap(change.NewValue as byte[]);
        }
        else if (change.Property == FitToViewportProperty
                 || change.Property == ZoomScaleProperty
                 || change.Property == BoundsProperty)
        {
            UpdateImageSize();
        }
        else if (change.Property == BackgroundHexProperty)
        {
            UpdateBackground();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        DisposeBitmap();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_bitmap is null && PreviewBytes is { Length: > 0 } bytes)
        {
            ReplaceBitmap(bytes);
        }

        UpdateBackground();
    }

    private void ReplaceBitmap(byte[]? bytes)
    {
        DisposeBitmap();
        if (bytes is not { Length: > 0 })
        {
            return;
        }

        using var stream = new MemoryStream(bytes, writable: false);
        _bitmap = new Bitmap(stream);
        _image.Source = _bitmap;
        UpdateImageSize();
    }

    private void UpdateImageSize()
    {
        if (_bitmap is null)
        {
            _image.Width = double.NaN;
            _image.Height = double.NaN;
            return;
        }

        var pixelWidth = _bitmap.PixelSize.Width;
        var pixelHeight = _bitmap.PixelSize.Height;
        var scale = Math.Clamp(ZoomScale, 0.25, 4d);
        if (FitToViewport && Bounds.Width > 0 && Bounds.Height > 0)
        {
            scale = Math.Min(Bounds.Width / pixelWidth, Bounds.Height / pixelHeight);
        }

        _image.Width = Math.Max(1, pixelWidth * scale);
        _image.Height = Math.Max(1, pixelHeight * scale);
        _imageHost.MinWidth = Math.Max(0, Bounds.Width);
        _imageHost.MinHeight = Math.Max(0, Bounds.Height);
    }

    private void UpdateBackground()
    {
        _imageHost.Background = Color.TryParse(BackgroundHex, out var color)
            ? new SolidColorBrush(color)
            : Brushes.Transparent;
    }

    private void DisposeBitmap()
    {
        _image.Source = null;
        _bitmap?.Dispose();
        _bitmap = null;
    }
}
