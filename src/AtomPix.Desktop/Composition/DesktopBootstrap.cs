namespace AtomPix.Desktop.Composition;

using AtomPix.Desktop.Platform;
using AtomPix.Desktop.Navigation;
using AtomPix.Desktop.Shell;
using AtomUI.City.Core.Hosting;
using AtomUI.City.Presentation;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;

internal static class DesktopBootstrap
{
    private static IApplicationHost? _host;

    internal static bool IsAttached => Volatile.Read(ref _host) is not null;

    internal static void Attach(IApplicationHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (Interlocked.CompareExchange(ref _host, host, null) is not null)
        {
            throw new InvalidOperationException("An AtomPix City Host is already attached.");
        }
    }

    internal static void Initialize(IClassicDesktopStyleApplicationLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        var host = Volatile.Read(ref _host)
            ?? throw new InvalidOperationException("The AtomPix City Host is not attached.");
        if (lifetime.MainWindow is not null)
        {
            throw new InvalidOperationException("The desktop lifetime already owns a main window.");
        }

        var runtime = host.Services.GetRequiredService<IPresentationRuntime>();
        runtime.Attach(lifetime, host.HostScope);
        var mainWindow = host.Services.GetRequiredService<MainWindow>();
        var session = runtime.RegisterWindow(mainWindow, "main");
        var outletRegistration = mainWindow.RegisterPrimaryOutlet(session);
        var shell = host.Services.GetRequiredService<ShellViewModel>();
        shell.AttachCompositionAsync(session).GetAwaiter().GetResult();
        var navigator = host.Services.GetRequiredService<AtomPixCityNavigator>();
        navigator.Attach(session.GetOutlet("primary"));
        var exceptionBoundary = host.Services.GetRequiredService<DesktopExceptionBoundary>();
        exceptionBoundary.Attach();
        EventHandler? openedHandler = null;
        openedHandler = (_, _) =>
        {
            mainWindow.Opened -= openedHandler;
            _ = PresentInitialRouteAsync(navigator, shell, exceptionBoundary);
        };
        mainWindow.Opened += openedHandler;
        mainWindow.Closed += (_, _) => _ = ShutdownWhenSessionCompletesAsync(
            session,
            lifetime,
            exceptionBoundary,
            outletRegistration,
            navigator);
        lifetime.MainWindow = mainWindow;
    }

    private static async Task PresentInitialRouteAsync(
        AtomPixCityNavigator navigator,
        ShellViewModel shell,
        DesktopExceptionBoundary exceptionBoundary)
    {
        try
        {
            var succeeded = await navigator.PresentAsync(
                new DesktopNavigationRequest(DesktopRoute.Browse),
                shell.Home);
            if (!succeeded)
            {
                throw new InvalidOperationException(
                    "The initial Home route could not be presented.",
                    navigator.LastResult?.Exception);
            }
        }
        catch (Exception exception)
        {
            exceptionBoundary.ReportStartupException(exception);
        }
    }

    internal static void Detach(IApplicationHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (!ReferenceEquals(Interlocked.CompareExchange(ref _host, null, host), host))
        {
            throw new InvalidOperationException("The supplied AtomPix City Host is not attached.");
        }
    }

    private static async Task ShutdownWhenSessionCompletesAsync(
        WindowSession session,
        IClassicDesktopStyleApplicationLifetime lifetime,
        DesktopExceptionBoundary exceptionBoundary,
        IDisposable outletRegistration,
        AtomPixCityNavigator navigator)
    {
        while (session.State is not (WindowSessionState.Closed or WindowSessionState.Faulted))
        {
            await Task.Delay(10).ConfigureAwait(false);
        }

        navigator.Dispose();
        outletRegistration.Dispose();
        exceptionBoundary.Dispose();
        var exitCode = session.State == WindowSessionState.Closed ? 0 : 1;
        Dispatcher.UIThread.Post(() => lifetime.Shutdown(exitCode));
    }
}
