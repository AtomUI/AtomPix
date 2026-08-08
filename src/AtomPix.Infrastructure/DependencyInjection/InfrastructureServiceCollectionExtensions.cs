namespace AtomPix.Infrastructure.DependencyInjection;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AtomPix.Core.Ports;
using AtomPix.Infrastructure.Configuration;
using AtomPix.Infrastructure.FileSystem;
using AtomPix.Infrastructure.Diagnostics;
using AtomPix.Infrastructure.Paths;
using AtomPix.Infrastructure.RecentItems;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddAtomPixInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IAppPathProvider, AppPathProvider>();
        services.AddSingleton<IAppSettingsStore, JsonAppSettingsStore>();
        services.AddSingleton<IRecentItemsStore, JsonRecentItemsStore>();
        services.AddSingleton<IFileSystemService, LocalFileSystemService>();
        AddDiagnostics(services);
        return services;
    }

    public static IServiceCollection AddAtomPixInfrastructure(this IServiceCollection services, string appDataDirectory, string tempDirectory)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IAppPathProvider>(new AppPathProvider(appDataDirectory, tempDirectory));
        services.AddSingleton<IAppSettingsStore, JsonAppSettingsStore>();
        services.AddSingleton<IRecentItemsStore, JsonRecentItemsStore>();
        services.AddSingleton<IFileSystemService, LocalFileSystemService>();
        AddDiagnostics(services);
        return services;
    }

    private static void AddDiagnostics(IServiceCollection services)
    {
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Information));
        services.AddSingleton<ILoggerProvider>(provider =>
        {
            var paths = provider.GetRequiredService<IAppPathProvider>();
            return new LocalJsonLoggerProvider(new LocalJsonLoggerOptions(Path.Combine(paths.AppDataDirectory.Value, "logs")));
        });
    }
}
