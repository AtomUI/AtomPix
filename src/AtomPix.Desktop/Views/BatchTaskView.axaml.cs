namespace AtomPix.Desktop.Views;

using AtomPix.Desktop.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

public sealed partial class BatchTaskView : UserControl
{
    public BatchTaskView() => AvaloniaXamlLoader.Load(this);

    private void HandleRemoveInput(object? sender, RoutedEventArgs e)
    {
        if (sender is AtomUI.Desktop.Controls.Button { CommandParameter: BatchItemViewModel item }
            && DataContext is BatchTaskViewModel viewModel)
        {
            viewModel.RemoveInputFromView(item);
        }
    }

    private void HandleViewDetails(object? sender, RoutedEventArgs e) =>
        ExecuteItemCommand(sender, viewModel => viewModel.ViewItemDetailsCommand);

    private void HandleRelocateInput(object? sender, RoutedEventArgs e) =>
        ExecuteItemCommand(sender, viewModel => viewModel.RelocateInputCommand);

    private void HandleCopyItemDiagnostic(object? sender, RoutedEventArgs e) =>
        ExecuteItemCommand(sender, viewModel => viewModel.CopyItemDiagnosticIdCommand);

    private void ExecuteItemCommand(
        object? sender,
        Func<BatchTaskViewModel, System.Windows.Input.ICommand> commandSelector)
    {
        if (sender is not AtomUI.Desktop.Controls.Button { CommandParameter: BatchItemViewModel item }
            || DataContext is not BatchTaskViewModel viewModel) return;
        var command = commandSelector(viewModel);
        if (command.CanExecute(item)) command.Execute(item);
    }
}
