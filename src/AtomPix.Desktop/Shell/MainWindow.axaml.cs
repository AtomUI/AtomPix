namespace AtomPix.Desktop.Shell;

using System.ComponentModel;
using AtomPix.Desktop.Navigation;
using AtomUI.Desktop.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Controls;

public sealed partial class MainWindow : AtomUI.Desktop.Controls.Window
{
    private bool _closeAuthorized;
    private bool _closePending;
    private NavMenu? _navigationMenu;
    private ShellViewModel? _observedViewModel;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        _navigationMenu = this.FindControl<NavMenu>("NavigationMenu");
        Activated += HandleActivated;
        Closing += HandleClosing;
        Closed += HandleClosed;
    }

    public MainWindow(ShellViewModel viewModel)
        : this()
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        DataContext = viewModel;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_observedViewModel is not null)
        {
            _observedViewModel.PropertyChanged -= HandleViewModelPropertyChanged;
        }

        base.OnDataContextChanged(e);
        _observedViewModel = DataContext as ShellViewModel;
        if (_observedViewModel is not null)
        {
            _observedViewModel.PropertyChanged += HandleViewModelPropertyChanged;
            SynchronizeNavigationSelection(_observedViewModel.CurrentRoute);
        }
    }

    private async void HandleClosing(object? sender, WindowClosingEventArgs args)
    {
        if (_closeAuthorized) return;
        args.Cancel = true;
        if (_closePending || DataContext is not ShellViewModel viewModel) return;

        _closePending = true;
        try
        {
            if (!await viewModel.TryCloseAsync()) return;
            _closeAuthorized = true;
            Close();
        }
        finally
        {
            _closePending = false;
        }
    }

    private void HandleViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (sender is ShellViewModel viewModel
            && args.PropertyName is nameof(ShellViewModel.CurrentRoute) or nameof(ShellViewModel.NavigationRevision))
        {
            SynchronizeNavigationSelection(viewModel.CurrentRoute);
        }
    }

    private void HandleActivated(object? sender, EventArgs args)
    {
        if (DataContext is ShellViewModel viewModel)
        {
            viewModel.RefreshResultAvailability();
        }
    }

    private void SynchronizeNavigationSelection(DesktopRoute route)
    {
        if (_navigationMenu is null)
        {
            return;
        }

        var node = _navigationMenu.Items
            .OfType<INavMenuNode>()
            .FirstOrDefault(item => item.CommandParameter is DesktopRoute itemRoute && itemRoute == route);
        if (node is not null && !ReferenceEquals(_navigationMenu.SelectedItem, node))
        {
            _navigationMenu.SelectedItem = node;
        }
    }

    private void HandleClosed(object? sender, EventArgs args)
    {
        Activated -= HandleActivated;
        if (_observedViewModel is not null)
        {
            _observedViewModel.PropertyChanged -= HandleViewModelPropertyChanged;
            _observedViewModel = null;
        }
    }
}
