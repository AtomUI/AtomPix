namespace AtomPix.Desktop.Views;

using AtomPix.Desktop.Controls;
using AtomPix.Desktop.ViewModels;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

public sealed partial class CropEditorView : UserControl
{
    public CropEditorView()
    {
        AvaloniaXamlLoader.Load(this);
        var canvas = this.FindControl<CropCanvas>("CropCanvas");
        if (canvas is not null)
        {
            canvas.SelectionChanged += HandleSelectionChanged;
        }
    }

    private void HandleSelectionChanged(object? sender, CropCanvasSelection selection)
    {
        if (DataContext is CropEditorViewModel viewModel)
        {
            viewModel.ApplyCanvasSelection(selection);
        }
    }
}
