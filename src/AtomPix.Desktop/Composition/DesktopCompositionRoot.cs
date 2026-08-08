namespace AtomPix.Desktop.Composition;

using AtomPix.Desktop.Navigation;
using AtomPix.Desktop.Platform;
using AtomPix.Desktop.Shell;
using AtomPix.Desktop.ViewModels;
using AtomPix.Imaging.Magick.DependencyInjection;
using AtomPix.Infrastructure.DependencyInjection;
using AtomPix.Workflows.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

public static class DesktopCompositionRoot
{
    public static ServiceProvider Build()
    {
        var services = new ServiceCollection();

        services.AddAtomPixInfrastructure();
        services.AddAtomPixMagickImaging();
        services.AddAtomPixWorkflows();

        services.AddSingleton<IDesktopPickerService, AvaloniaDesktopPickerService>();
        services.AddSingleton<IDesktopLauncherService, AvaloniaDesktopLauncherService>();
        services.AddSingleton<IDesktopDialogService, AvaloniaDesktopDialogService>();
        services.AddSingleton<IDesktopClipboardService, AvaloniaDesktopClipboardService>();
        services.AddSingleton<IDesktopAppearanceService, AvaloniaDesktopAppearanceService>();
        services.AddSingleton<IDesktopDispatcher, AvaloniaDesktopDispatcher>();
        services.AddSingleton<ResultOutputGuard>();
        services.AddSingleton<DesktopExceptionBoundary>();
        services.AddSingleton<DesktopNavigationCoordinator>();
        services.AddSingleton<IDesktopNavigator>(provider => provider.GetRequiredService<DesktopNavigationCoordinator>());

        services.AddSingleton<HomePageViewModel>();
        services.AddSingleton<ImageBrowserViewModel>();
        services.AddSingleton<CompressionEditorViewModel>();
        services.AddSingleton<ConversionEditorViewModel>();
        services.AddSingleton<ResizeEditorViewModel>();
        services.AddSingleton<CropEditorViewModel>();
        services.AddSingleton<BatchTaskViewModel>();
        services.AddSingleton<SettingsPageViewModel>();
        services.AddSingleton<ShellViewModel>();

        return services.BuildServiceProvider(validateScopes: true);
    }
}
