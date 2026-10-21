namespace AtomPix.Desktop.Composition;

using System.Globalization;
using AtomPix.Desktop.Localization;
using AtomPix.Desktop.Navigation;
using AtomPix.Desktop.Platform;
using AtomPix.Desktop.Shell;
using AtomPix.Desktop.ViewModels;
using AtomPix.Imaging.Magick.DependencyInjection;
using AtomPix.Infrastructure.DependencyInjection;
using AtomPix.Workflows.DependencyInjection;
using AtomUI.City.Core.Modularity;
using AtomUI.City.Localization;
using AtomUI.City.Presentation;
using AtomUI.City.Routing;
using Microsoft.Extensions.DependencyInjection;

[Module(
    "AtomPix.Desktop",
    Version = "0.1.0",
    Description = "AtomPix desktop composition and presentation module.")]
[DependsOn(typeof(AtomPixWorkflowsModule))]
[DependsOn(typeof(RoutingModule))]
[DependsOn(typeof(PresentationModule))]
public sealed class AtomPixDesktopModule : ModuleBase
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var services = context.Services;

        services.AddAtomPixInfrastructure();
        services.AddAtomPixMagickImaging();
        services.AddLocalization(options =>
        {
            var fallback = CultureInfo.GetCultureInfo("zh-CN");
            options.DefaultCulture = fallback;
            options.DefaultUICulture = fallback;
        });
        services.AddSingleton<IPresentationLocalizationBridge, AvaloniaPresentationLocalizationBridge>();
        services.AddRouting(AtomPixRoutes.Descriptors);

        services.AddSingleton<IDesktopPickerService, AvaloniaDesktopPickerService>();
        services.AddSingleton<IDesktopLauncherService, AvaloniaDesktopLauncherService>();
        services.AddSingleton<IDesktopDialogService, AvaloniaDesktopDialogService>();
        services.AddSingleton<IDesktopClipboardService, AvaloniaDesktopClipboardService>();
        services.AddSingleton<IDesktopDispatcher, AvaloniaDesktopDispatcher>();
        services.AddSingleton<AvaloniaDesktopFeedbackService>();
        services.AddSingleton<IDesktopFeedbackService>(provider =>
            provider.GetRequiredService<AvaloniaDesktopFeedbackService>());
        services.AddSingleton<ResultOutputGuard>();
        services.AddSingleton<DesktopExceptionBoundary>();
        services.AddSingleton<DesktopNavigationCoordinator>();
        services.AddSingleton<IDesktopNavigator>(provider =>
            provider.GetRequiredService<DesktopNavigationCoordinator>());
        services.AddSingleton<BrowserNavigationSessionStore>();
        services.AddSingleton<AtomPixViewCatalog>();
        services.AddSingleton<AtomPixCityNavigator>();

        services.AddSingleton<HomePageViewModel>();
        services.AddSingleton<ImageBrowserViewModel>();
        services.AddSingleton<CompressionEditorViewModel>();
        services.AddSingleton<ConversionEditorViewModel>();
        services.AddSingleton<ResizeEditorViewModel>();
        services.AddSingleton<CropEditorViewModel>();
        services.AddSingleton<BatchTaskViewModel>();
        services.AddSingleton<SettingsPageViewModel>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<MainWindow>();
    }

    public override void OnApplicationInitialization(ApplicationInitializationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        AtomUI.City.Generated.GeneratedLocalizationManifest.RegisterPackages(
            context.Services.GetRequiredService<LanguagePackageRegistry>(),
            AtomPixLocalization.OwnerId);
        context.Services.GetRequiredService<AtomPixViewCatalog>().Register();
    }
}
