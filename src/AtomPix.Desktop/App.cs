namespace AtomPix.Desktop;

using System.Globalization;
using AtomPix.Desktop.Composition;
using AtomPix.Desktop.Shell;
using AtomPix.Desktop.Platform;
using AtomUI;
using AtomUI.Desktop.Controls;
using AtomUI.Theme;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.DependencyInjection;

public sealed class App : Application
{
    private ServiceProvider? _services;
    private DesktopExceptionBoundary? _exceptionBoundary;

    public override void Initialize()
    {
        this.UseAtomUI(builder =>
        {
            builder.WithApplicationId("AtomPix");
            builder.WithDefaultCultureInfo(CultureInfo.GetCultureInfo("zh-CN"));
            builder.WithInitialTheme(IThemeManager.DEFAULT_THEME_ID);
            builder.UseAlibabaSansFont();
            builder.UseDesktopControls();
            builder.UseDesktopColorPicker();
        });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _services = DesktopCompositionRoot.Build();
            desktop.MainWindow = new MainWindow(_services.GetRequiredService<ShellViewModel>());
            _exceptionBoundary = _services.GetRequiredService<DesktopExceptionBoundary>();
            _exceptionBoundary.Attach();
            desktop.Exit += HandleExit;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void HandleExit(object? sender, ControlledApplicationLifetimeExitEventArgs args)
    {
        _exceptionBoundary?.Dispose();
        _exceptionBoundary = null;
        _services?.Dispose();
        _services = null;
    }
}
