namespace AtomPix.Desktop;

using System.Globalization;
using AtomPix.Desktop.Composition;
using AtomUI;
using AtomUI.Desktop.Controls;
using AtomUI.Labs.Controls.ImageGallery;
using AtomUI.Theme;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

public sealed class App : Application
{
    public override void Initialize()
    {
        var defaultCulture = CultureInfo.GetCultureInfo("zh-CN");
        CultureInfo.DefaultThreadCurrentCulture = defaultCulture;
        CultureInfo.DefaultThreadCurrentUICulture = defaultCulture;
        this.UseAtomUI(builder =>
        {
            builder.WithDefaultCultureInfo(defaultCulture);
            builder.UseDesktopControls();
            builder.UseDesktopColorPicker();
            builder.UseImageGallery();
        });

        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && DesktopBootstrap.IsAttached)
        {
            DesktopBootstrap.Initialize(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
