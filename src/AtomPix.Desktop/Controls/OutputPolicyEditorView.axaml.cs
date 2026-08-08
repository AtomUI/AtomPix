namespace AtomPix.Desktop.Controls;

using AtomPix.Desktop.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

public sealed partial class OutputPolicyEditorView : UserControl
{
    public OutputPolicyEditorView() => AvaloniaXamlLoader.Load(this);

    private void InsertNameTokenClick(object? sender, RoutedEventArgs e) => InsertToken("{name}");

    private void InsertIndexTokenClick(object? sender, RoutedEventArgs e) => InsertToken("{index}");

    private void InsertToken(string token)
    {
        if (DataContext is not OutputPolicyEditorViewModel viewModel) return;
        var textBox = this.FindControl<AtomUI.Desktop.Controls.TextBox>("FileNamePatternTextBox");
        if (textBox is null) return;

        var caret = viewModel.InsertTokenAt(token, textBox.SelectionStart, textBox.SelectionEnd);
        textBox.SelectionStart = caret;
        textBox.SelectionEnd = caret;
        textBox.Focus();
    }
}
