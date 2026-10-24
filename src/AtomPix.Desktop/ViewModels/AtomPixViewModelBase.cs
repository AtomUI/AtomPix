namespace AtomPix.Desktop.ViewModels;

using System.ComponentModel;

/// <summary>
/// Isolates AtomPix view models from source-level notification API changes in City.
/// </summary>
public abstract class AtomPixViewModelBase : ViewModelBase
{
    /// <summary>
    /// Raises a property-changing notification by property name.
    /// </summary>
    protected void OnPropertyChanging(string? propertyName) =>
        base.OnPropertyChanging(new PropertyChangingEventArgs(propertyName));

    /// <summary>
    /// Raises a property-changed notification by property name.
    /// </summary>
    protected void OnPropertyChanged(string? propertyName) =>
        base.OnPropertyChanged(new PropertyChangedEventArgs(propertyName));
}
