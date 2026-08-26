namespace AtomPix.Desktop.Controls;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

public sealed record CropCanvasSelection(int X, int Y, int Width, int Height);

public sealed class CropCanvas : Control
{
    public static readonly StyledProperty<byte[]?> PreviewBytesProperty =
        AvaloniaProperty.Register<CropCanvas, byte[]?>(nameof(PreviewBytes));
    public static readonly StyledProperty<IImage?> ImageSourceProperty =
        AvaloniaProperty.Register<CropCanvas, IImage?>(nameof(ImageSource));
    public static readonly StyledProperty<int> ImagePixelWidthProperty =
        AvaloniaProperty.Register<CropCanvas, int>(nameof(ImagePixelWidth));
    public static readonly StyledProperty<int> ImagePixelHeightProperty =
        AvaloniaProperty.Register<CropCanvas, int>(nameof(ImagePixelHeight));
    public static readonly StyledProperty<int> CropXProperty =
        AvaloniaProperty.Register<CropCanvas, int>(nameof(CropX));
    public static readonly StyledProperty<int> CropYProperty =
        AvaloniaProperty.Register<CropCanvas, int>(nameof(CropY));
    public static readonly StyledProperty<int> CropWidthProperty =
        AvaloniaProperty.Register<CropCanvas, int>(nameof(CropWidth), 1);
    public static readonly StyledProperty<int> CropHeightProperty =
        AvaloniaProperty.Register<CropCanvas, int>(nameof(CropHeight), 1);
    public static readonly StyledProperty<double> LockedAspectRatioProperty =
        AvaloniaProperty.Register<CropCanvas, double>(nameof(LockedAspectRatio));
    public static readonly StyledProperty<bool> IsInteractionEnabledProperty =
        AvaloniaProperty.Register<CropCanvas, bool>(nameof(IsInteractionEnabled), true);
    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        AvaloniaProperty.Register<CropCanvas, IBrush?>(nameof(Background));
    public static readonly StyledProperty<IBrush?> ImageBorderBrushProperty =
        AvaloniaProperty.Register<CropCanvas, IBrush?>(nameof(ImageBorderBrush));

    private const double HandleSize = 12;
    private Bitmap? _bitmap;
    private CropDragHandle _dragHandle;
    private Point _dragStart;
    private Rect _dragStartPixels;

    public CropCanvas()
    {
        Focusable = true;
        AutomationProperties.SetName(this, "裁剪选区画布");
        AutomationProperties.SetHelpText(this, "使用方向键移动选区一像素，按住 Shift 时移动十像素。拖动选区或八个控制点可调整裁剪区域。");
    }

    public event EventHandler<CropCanvasSelection>? SelectionChanged;

    public byte[]? PreviewBytes { get => GetValue(PreviewBytesProperty); set => SetValue(PreviewBytesProperty, value); }
    /// <summary>
    /// Non-owning decoded image supplied by the ImageGallery lease bridge.
    /// The caller must keep its lease alive until this property is replaced or cleared.
    /// </summary>
    public IImage? ImageSource { get => GetValue(ImageSourceProperty); set => SetValue(ImageSourceProperty, value); }
    public int ImagePixelWidth { get => GetValue(ImagePixelWidthProperty); set => SetValue(ImagePixelWidthProperty, value); }
    public int ImagePixelHeight { get => GetValue(ImagePixelHeightProperty); set => SetValue(ImagePixelHeightProperty, value); }
    public int CropX { get => GetValue(CropXProperty); set => SetValue(CropXProperty, value); }
    public int CropY { get => GetValue(CropYProperty); set => SetValue(CropYProperty, value); }
    public int CropWidth { get => GetValue(CropWidthProperty); set => SetValue(CropWidthProperty, value); }
    public int CropHeight { get => GetValue(CropHeightProperty); set => SetValue(CropHeightProperty, value); }
    public double LockedAspectRatio { get => GetValue(LockedAspectRatioProperty); set => SetValue(LockedAspectRatioProperty, value); }
    public bool IsInteractionEnabled { get => GetValue(IsInteractionEnabledProperty); set => SetValue(IsInteractionEnabledProperty, value); }
    public IBrush? Background { get => GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }
    public IBrush? ImageBorderBrush { get => GetValue(ImageBorderBrushProperty); set => SetValue(ImageBorderBrushProperty, value); }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Background is { } background)
        {
            context.FillRectangle(background, Bounds);
        }
        var image = ImageSource ?? _bitmap;
        if (image is null || ImagePixelWidth <= 0 || ImagePixelHeight <= 0)
        {
            return;
        }

        var imageRect = GetImageRect();
        context.DrawImage(image, new Rect(image.Size), imageRect);
        if (ImageBorderBrush is { } imageBorderBrush)
        {
            context.DrawRectangle(null, new Pen(imageBorderBrush, 1), imageRect);
        }
        var selection = PixelsToView(new Rect(CropX, CropY, CropWidth, CropHeight), imageRect);
        var shade = new SolidColorBrush(Color.FromArgb(158, 16, 22, 34));
        context.FillRectangle(shade, new Rect(imageRect.X, imageRect.Y, imageRect.Width, Math.Max(0, selection.Y - imageRect.Y)));
        context.FillRectangle(shade, new Rect(imageRect.X, selection.Bottom, imageRect.Width, Math.Max(0, imageRect.Bottom - selection.Bottom)));
        context.FillRectangle(shade, new Rect(imageRect.X, selection.Y, Math.Max(0, selection.X - imageRect.X), selection.Height));
        context.FillRectangle(shade, new Rect(selection.Right, selection.Y, Math.Max(0, imageRect.Right - selection.Right), selection.Height));

        var linePen = new Pen(Brushes.White, 1.5);
        context.DrawRectangle(null, linePen, selection);
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)), 1);
        context.DrawLine(gridPen, new Point(selection.X + selection.Width / 3, selection.Y), new Point(selection.X + selection.Width / 3, selection.Bottom));
        context.DrawLine(gridPen, new Point(selection.X + selection.Width * 2 / 3, selection.Y), new Point(selection.X + selection.Width * 2 / 3, selection.Bottom));
        context.DrawLine(gridPen, new Point(selection.X, selection.Y + selection.Height / 3), new Point(selection.Right, selection.Y + selection.Height / 3));
        context.DrawLine(gridPen, new Point(selection.X, selection.Y + selection.Height * 2 / 3), new Point(selection.Right, selection.Y + selection.Height * 2 / 3));

        foreach (var handle in GetHandleRects(selection).Values)
        {
            context.DrawRectangle(Brushes.White, new Pen(new SolidColorBrush(Color.Parse("#4F6BED")), 2), handle, 2, 2);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PreviewBytesProperty)
        {
            ReplaceBitmap(change.NewValue as byte[]);
        }
        else if (change.Property == ImageSourceProperty
            || change.Property == BackgroundProperty
            || change.Property == ImageBorderBrushProperty)
        {
            InvalidateVisual();
        }
        else if (change.Property == ImagePixelWidthProperty
            || change.Property == ImagePixelHeightProperty
            || change.Property == CropXProperty
            || change.Property == CropYProperty
            || change.Property == CropWidthProperty
            || change.Property == CropHeightProperty
            || change.Property == LockedAspectRatioProperty)
        {
            InvalidateVisual();
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new CropCanvasAutomationPeer(this);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsInteractionEnabled || (ImageSource is null && _bitmap is null) || ImagePixelWidth <= 0 || ImagePixelHeight <= 0)
        {
            return;
        }

        Focus();

        var point = e.GetPosition(this);
        var imageRect = GetImageRect();
        var selection = PixelsToView(new Rect(CropX, CropY, CropWidth, CropHeight), imageRect);
        _dragHandle = HitTestHandle(point, selection);
        if (_dragHandle == CropDragHandle.None && selection.Contains(point))
        {
            _dragHandle = CropDragHandle.Move;
        }
        if (_dragHandle == CropDragHandle.None)
        {
            return;
        }

        _dragStart = point;
        _dragStartPixels = new Rect(CropX, CropY, CropWidth, CropHeight);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!IsInteractionEnabled || (ImageSource is null && _bitmap is null) || ImagePixelWidth <= 0 || ImagePixelHeight <= 0)
        {
            return;
        }

        var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
        var (dx, dy) = e.Key switch
        {
            Key.Left => (-step, 0),
            Key.Right => (step, 0),
            Key.Up => (0, -step),
            Key.Down => (0, step),
            _ => (0, 0)
        };
        if (dx == 0 && dy == 0)
        {
            return;
        }

        var current = new Rect(CropX, CropY, CropWidth, CropHeight);
        Publish(AdjustSelection(current, dx, dy, CropDragHandle.Move, LockedAspectRatio));
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragHandle == CropDragHandle.None || e.Pointer.Captured != this)
        {
            return;
        }

        var imageRect = GetImageRect();
        if (imageRect.Width <= 0 || imageRect.Height <= 0)
        {
            return;
        }
        var current = e.GetPosition(this);
        var dx = (current.X - _dragStart.X) * ImagePixelWidth / imageRect.Width;
        var dy = (current.Y - _dragStart.Y) * ImagePixelHeight / imageRect.Height;
        var adjusted = AdjustSelection(_dragStartPixels, dx, dy, _dragHandle, LockedAspectRatio);
        Publish(adjusted);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.Pointer.Captured == this)
        {
            e.Pointer.Capture(null);
        }
        _dragHandle = CropDragHandle.None;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        DisposeBitmap();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_bitmap is null && PreviewBytes is { Length: > 0 } bytes) ReplaceBitmap(bytes);
    }

    private Rect AdjustSelection(Rect start, double dx, double dy, CropDragHandle handle, double ratio)
    {
        if (handle == CropDragHandle.Move)
        {
            var x = Math.Clamp(start.X + dx, 0, ImagePixelWidth - start.Width);
            var y = Math.Clamp(start.Y + dy, 0, ImagePixelHeight - start.Height);
            return new Rect(x, y, start.Width, start.Height);
        }

        var left = start.Left;
        var top = start.Top;
        var right = start.Right;
        var bottom = start.Bottom;
        if (handle is CropDragHandle.NorthWest or CropDragHandle.West or CropDragHandle.SouthWest) left += dx;
        if (handle is CropDragHandle.NorthEast or CropDragHandle.East or CropDragHandle.SouthEast) right += dx;
        if (handle is CropDragHandle.NorthWest or CropDragHandle.North or CropDragHandle.NorthEast) top += dy;
        if (handle is CropDragHandle.SouthWest or CropDragHandle.South or CropDragHandle.SouthEast) bottom += dy;
        left = Math.Clamp(left, 0, right - 1);
        top = Math.Clamp(top, 0, bottom - 1);
        right = Math.Clamp(right, left + 1, ImagePixelWidth);
        bottom = Math.Clamp(bottom, top + 1, ImagePixelHeight);

        if (ratio > 0)
        {
            ApplyRatio(ref left, ref top, ref right, ref bottom, handle, ratio);
        }
        return new Rect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    private void ApplyRatio(ref double left, ref double top, ref double right, ref double bottom, CropDragHandle handle, double ratio)
    {
        var width = Math.Max(1, right - left);
        var height = Math.Max(1, bottom - top);
        if (handle is CropDragHandle.North or CropDragHandle.South)
        {
            width = height * ratio;
            var centerX = (left + right) / 2;
            left = centerX - width / 2;
            right = centerX + width / 2;
        }
        else
        {
            height = width / ratio;
            if (handle is CropDragHandle.West or CropDragHandle.East)
            {
                var centerY = (top + bottom) / 2;
                top = centerY - height / 2;
                bottom = centerY + height / 2;
            }
            else if (handle is CropDragHandle.NorthWest or CropDragHandle.NorthEast)
            {
                top = bottom - height;
            }
            else
            {
                bottom = top + height;
            }
        }

        if (left < 0) { right -= left; left = 0; }
        if (right > ImagePixelWidth) { left -= right - ImagePixelWidth; right = ImagePixelWidth; }
        if (top < 0) { bottom -= top; top = 0; }
        if (bottom > ImagePixelHeight) { top -= bottom - ImagePixelHeight; bottom = ImagePixelHeight; }
        left = Math.Max(0, left);
        top = Math.Max(0, top);
    }

    private void Publish(Rect selection)
    {
        var width = Math.Clamp((int)Math.Round(selection.Width), 1, ImagePixelWidth);
        var height = Math.Clamp((int)Math.Round(selection.Height), 1, ImagePixelHeight);
        var x = Math.Clamp((int)Math.Round(selection.X), 0, ImagePixelWidth - width);
        var y = Math.Clamp((int)Math.Round(selection.Y), 0, ImagePixelHeight - height);
        CropX = x;
        CropY = y;
        CropWidth = width;
        CropHeight = height;
        SelectionChanged?.Invoke(this, new CropCanvasSelection(x, y, width, height));
    }

    private CropDragHandle HitTestHandle(Point point, Rect selection)
    {
        foreach (var (handle, rect) in GetHandleRects(selection))
        {
            if (rect.Inflate(4).Contains(point)) return handle;
        }
        return CropDragHandle.None;
    }

    private static Dictionary<CropDragHandle, Rect> GetHandleRects(Rect selection)
    {
        var half = HandleSize / 2;
        Rect At(double x, double y) => new(x - half, y - half, HandleSize, HandleSize);
        return new Dictionary<CropDragHandle, Rect>
        {
            [CropDragHandle.NorthWest] = At(selection.Left, selection.Top),
            [CropDragHandle.North] = At(selection.Center.X, selection.Top),
            [CropDragHandle.NorthEast] = At(selection.Right, selection.Top),
            [CropDragHandle.West] = At(selection.Left, selection.Center.Y),
            [CropDragHandle.East] = At(selection.Right, selection.Center.Y),
            [CropDragHandle.SouthWest] = At(selection.Left, selection.Bottom),
            [CropDragHandle.South] = At(selection.Center.X, selection.Bottom),
            [CropDragHandle.SouthEast] = At(selection.Right, selection.Bottom)
        };
    }

    private Rect GetImageRect()
    {
        if (ImagePixelWidth <= 0 || ImagePixelHeight <= 0) return default;
        const double padding = 18;
        var availableWidth = Math.Max(0, Bounds.Width - padding * 2);
        var availableHeight = Math.Max(0, Bounds.Height - padding * 2);
        var scale = Math.Min(availableWidth / ImagePixelWidth, availableHeight / ImagePixelHeight);
        var width = ImagePixelWidth * scale;
        var height = ImagePixelHeight * scale;
        return new Rect((Bounds.Width - width) / 2, (Bounds.Height - height) / 2, width, height);
    }

    private Rect PixelsToView(Rect pixels, Rect imageRect) => new(
        imageRect.X + pixels.X * imageRect.Width / ImagePixelWidth,
        imageRect.Y + pixels.Y * imageRect.Height / ImagePixelHeight,
        pixels.Width * imageRect.Width / ImagePixelWidth,
        pixels.Height * imageRect.Height / ImagePixelHeight);

    private void ReplaceBitmap(byte[]? bytes)
    {
        DisposeBitmap();
        if (bytes is not { Length: > 0 }) return;
        using var stream = new MemoryStream(bytes, writable: false);
        _bitmap = new Bitmap(stream);
        InvalidateVisual();
    }

    private void DisposeBitmap()
    {
        _bitmap?.Dispose();
        _bitmap = null;
    }

    private enum CropDragHandle
    {
        None,
        Move,
        NorthWest,
        North,
        NorthEast,
        West,
        East,
        SouthWest,
        South,
        SouthEast
    }

    private sealed class CropCanvasAutomationPeer(CropCanvas owner) : ControlAutomationPeer(owner)
    {
        protected override string GetNameCore() => "裁剪选区画布";

        protected override string GetClassNameCore() => nameof(CropCanvas);

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Custom;
    }
}
