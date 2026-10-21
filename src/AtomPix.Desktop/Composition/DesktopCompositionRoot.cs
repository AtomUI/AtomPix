namespace AtomPix.Desktop.Composition;

using AtomPix.Workflows.DependencyInjection;
using AtomUI.City.Core.Hosting;
using AtomUI.City.Core.Modularity;
using AtomUI.City.Presentation;
using AtomUI.City.Routing;

public static class DesktopCompositionRoot
{
    public static IApplicationHostBuilder CreateBuilder(string[]? args = null)
    {
        var builder = ApplicationHost.CreateBuilder(args ?? []);
        builder.ConfigureHost(options =>
        {
            options.ApplicationId = "AtomPix";
            options.ApplicationName = "AtomPix";
            options.ApplicationVersion = "0.1.0";
        });
        builder
            .UseModule<RoutingModule>()
            .UseModule<PresentationModule>()
            .UseModule<AtomPixWorkflowsModule>()
            .UseModule<AtomPixDesktopModule>();
        return builder;
    }

    public static IApplicationHost CreateHost(string[]? args = null) =>
        CreateBuilder(args).Build();

    public static DesktopServiceHost Build(string[]? args = null)
    {
        var host = CreateHost(args);
        try
        {
            host.StartAsync().GetAwaiter().GetResult();
            return new DesktopServiceHost(host);
        }
        catch
        {
            host.Dispose();
            throw;
        }
    }
}

public sealed class DesktopServiceHost : IServiceProvider, IDisposable
{
    private readonly IApplicationHost _host;
    private bool _disposed;

    internal DesktopServiceHost(IApplicationHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    public IApplicationHost ApplicationHost => _host;

    public object? GetService(Type serviceType)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _host.Services.GetService(serviceType);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            Task.Run(() => _host.StopAsync()).GetAwaiter().GetResult();
        }
        finally
        {
            _host.Dispose();
        }
    }
}
