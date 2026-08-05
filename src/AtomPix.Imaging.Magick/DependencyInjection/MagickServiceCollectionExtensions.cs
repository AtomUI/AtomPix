namespace AtomPix.Imaging.Magick.DependencyInjection;

using Microsoft.Extensions.DependencyInjection;
using AtomPix.Imaging.Abstractions.Processing;
using AtomPix.Imaging.Magick.Processing;

public static class MagickServiceCollectionExtensions
{
    public static IServiceCollection AddAtomPixMagickImaging(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IImageProcessor, MagickImageProcessor>();
        return services;
    }
}
