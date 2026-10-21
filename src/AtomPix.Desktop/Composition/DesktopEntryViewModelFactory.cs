namespace AtomPix.Desktop.Composition;

using AtomPix.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

public interface IDesktopEntryViewModelFactory
{
    HomePageViewModel CreateHome();

    SettingsPageViewModel CreateSettings();
}

public sealed class DesktopEntryViewModelFactory(
    IServiceProvider services) : IDesktopEntryViewModelFactory
{
    public HomePageViewModel CreateHome() =>
        services.GetRequiredService<HomePageViewModel>();

    public SettingsPageViewModel CreateSettings() =>
        services.GetRequiredService<SettingsPageViewModel>();
}
