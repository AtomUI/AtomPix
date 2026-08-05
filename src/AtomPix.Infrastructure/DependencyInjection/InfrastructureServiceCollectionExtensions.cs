namespace AtomPix.Infrastructure.DependencyInjection;

using Microsoft.Extensions.DependencyInjection;
using AtomPix.Core.Ports;
using AtomPix.Infrastructure.Configuration;
using AtomPix.Infrastructure.FileSystem;
using AtomPix.Infrastructure.Paths;
using AtomPix.Infrastructure.RecentItems;
using AtomPix.Infrastructure.Subscriptions;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddAtomPixInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IAppPathProvider, AppPathProvider>();
        services.AddSingleton<IAppSettingsStore, JsonAppSettingsStore>();
        services.AddSingleton<ISubscriptionStore, LocalSubscriptionStore>();
        services.AddSingleton<IRecentItemsStore, JsonRecentItemsStore>();
        services.AddSingleton<IFileSystemService, LocalFileSystemService>();
        return services;
    }

    public static IServiceCollection AddAtomPixInfrastructure(this IServiceCollection services, string appDataDirectory, string tempDirectory)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IAppPathProvider>(new AppPathProvider(appDataDirectory, tempDirectory));
        services.AddSingleton<IAppSettingsStore, JsonAppSettingsStore>();
        services.AddSingleton<ISubscriptionStore, LocalSubscriptionStore>();
        services.AddSingleton<IRecentItemsStore, JsonRecentItemsStore>();
        services.AddSingleton<IFileSystemService, LocalFileSystemService>();
        return services;
    }
}
