using AtomPix.Desktop;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;

namespace AtomPix.Desktop.UiTests;

public static class UiTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                // Skia-backed headless drawing is required for frame capture assertions.
                UseHeadlessDrawing = false,
                ShouldRenderOnUIThread = true
            });
}
