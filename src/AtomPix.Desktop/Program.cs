namespace AtomPix.Desktop;

using AtomUI;
using Avalonia;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<App>()
        .UseAtomUIPlatformDetect(AtomUIWindowingPlatform.Auto)
        .WithAtomUIDefaultOptions()
        .LogToTrace();
}
