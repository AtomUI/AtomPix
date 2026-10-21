namespace AtomPix.Desktop;

using AtomPix.Desktop.Composition;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        using var host = DesktopCompositionRoot.CreateHost(args);
        host.StartAsync().GetAwaiter().GetResult();
        DesktopBootstrap.Attach(host);
        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(
                args,
                ShutdownMode.OnExplicitShutdown);
        }
        finally
        {
            DesktopBootstrap.Detach(host);
            Task.Run(() => host.StopAsync()).GetAwaiter().GetResult();
        }
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<App>()
        .UsePlatformDetect()
        .LogToTrace();
}
