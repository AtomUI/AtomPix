namespace AtomPix.Imaging.Magick.DependencyInjection;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AtomPix.Core.Ports;
using AtomPix.Imaging.Abstractions.Processing;
using AtomPix.Imaging.Magick.Processing;

public static class MagickServiceCollectionExtensions
{
    public static IServiceCollection AddAtomPixMagickImaging(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IImageFileCommitter, AtomicImageFileCommitter>();
        services.AddSingleton<IImageProcessor>(provider =>
        {
            var paths = provider.GetRequiredService<IAppPathProvider>();
            var options = MagickImageProcessorOptions.CreateDefault(Path.Combine(paths.TempDirectory.Value, "Magick"));
            MagickRuntime.Configure(options);
            return new MagickImageProcessor(
                options,
                provider.GetService<ILogger<MagickImageProcessor>>(),
                provider.GetRequiredService<IImageFileCommitter>());
        });
        return services;
    }
}
