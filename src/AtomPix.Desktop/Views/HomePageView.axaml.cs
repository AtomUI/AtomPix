namespace AtomPix.Desktop.Views;

using AtomPix.Desktop.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Interactivity;

public sealed partial class HomePageView : UserControl
{
    public HomePageView()
    {
        AvaloniaXamlLoader.Load(this);
        AddHandler(DragDrop.DragEnterEvent, SourceDragEnterOrOver, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
        AddHandler(DragDrop.DragOverEvent, SourceDragEnterOrOver, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
        AddHandler(DragDrop.DragLeaveEvent, SourceDragLeave, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
        AddHandler(DragDrop.DropEvent, SourceDrop, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
    }

    private void SourceDragEnterOrOver(object? sender, DragEventArgs args)
    {
        var viewModel = DataContext as HomePageViewModel;
        var canAccept = viewModel is { CanAcceptDrop: true }
            && args.DataTransfer.Contains(DataFormat.File);
        args.DragEffects = canAccept ? DragDropEffects.Copy : DragDropEffects.None;
        args.Handled = true;
        viewModel?.SetDragOver(canAccept);
    }

    private void SourceDragLeave(object? sender, DragEventArgs args)
    {
        if (DataContext is HomePageViewModel viewModel)
        {
            viewModel.SetDragOver(false);
        }

        args.Handled = true;
    }

    private void SourceDrop(object? sender, DragEventArgs args)
    {
        if (DataContext is not HomePageViewModel viewModel)
        {
            args.DragEffects = DragDropEffects.None;
            args.Handled = true;
            return;
        }

        viewModel.SetDragOver(false);
        string[] paths;
        try
        {
            paths = args.DataTransfer.TryGetFiles()?
                .Select(item => item.TryGetLocalPath())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Cast<string>()
                .ToArray() ?? [];
        }
        catch
        {
            args.DragEffects = DragDropEffects.None;
            args.Handled = true;
            viewModel.ReportDropFailure();
            return;
        }

        args.DragEffects = paths.Length > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        args.Handled = true;
        viewModel.OpenDroppedSourcesCommand.Execute(paths);
    }
}
