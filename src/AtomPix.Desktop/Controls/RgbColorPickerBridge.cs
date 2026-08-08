namespace AtomPix.Desktop.Controls;

using AtomUI.Desktop.Controls;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;

public sealed class RgbColorPickerBridge : UserControl
{
    public static readonly StyledProperty<string> HexValueProperty =
        AvaloniaProperty.Register<RgbColorPickerBridge, string>(
            nameof(HexValue),
            "#FFFFFF",
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    private readonly AtomUI.Desktop.Controls.ColorPicker _picker;
    private bool _synchronizing;

    public RgbColorPickerBridge()
    {
        _picker = new AtomUI.Desktop.Controls.ColorPicker
        {
            IsAlphaEnabled = false,
            IsClearEnabled = false,
            IsTextVisible = false,
            Value = Color.Parse("#FFFFFF")
        };
        AutomationProperties.SetName(this, "透明区域背景颜色");
        AutomationProperties.SetName(_picker, "透明区域背景颜色");
        AutomationProperties.SetHelpText(_picker, "选择透明像素转换到不支持透明通道的格式时使用的背景颜色。");
        _picker.ValueChanged += HandleValueChanged;
        Content = _picker;
    }

    public string HexValue
    {
        get => GetValue(HexValueProperty);
        set => SetValue(HexValueProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != HexValueProperty || _synchronizing)
        {
            return;
        }

        var value = change.NewValue as string;
        if (!string.IsNullOrWhiteSpace(value) && Color.TryParse(value, out var color))
        {
            _synchronizing = true;
            _picker.Value = Color.FromRgb(color.R, color.G, color.B);
            _synchronizing = false;
        }
    }

    private void HandleValueChanged(object? sender, AtomUI.Desktop.Controls.ColorChangedEventArgs args)
    {
        if (_synchronizing || args.NewColor is not { } color)
        {
            return;
        }

        _synchronizing = true;
        SetCurrentValue(HexValueProperty, $"#{color.R:X2}{color.G:X2}{color.B:X2}");
        _synchronizing = false;
    }
}
