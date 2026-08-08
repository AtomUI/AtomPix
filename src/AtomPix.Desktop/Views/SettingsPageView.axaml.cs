namespace AtomPix.Desktop.Views;

using AtomPix.Desktop.ViewModels;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

public sealed partial class SettingsPageView : Avalonia.Controls.UserControl
{
    public SettingsPageView() => AvaloniaXamlLoader.Load(this);

    private void UseWhiteClick(object? sender, RoutedEventArgs args)
    {
        if (DataContext is SettingsPageViewModel viewModel) viewModel.BackgroundHex = "#FFFFFF";
    }

    private void UseBlackClick(object? sender, RoutedEventArgs args)
    {
        if (DataContext is SettingsPageViewModel viewModel) viewModel.BackgroundHex = "#000000";
    }
}
