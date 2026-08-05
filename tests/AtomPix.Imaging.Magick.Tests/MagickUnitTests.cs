namespace AtomPix.Imaging.Magick.Tests;

using AtomPix.Core.Conversion;
using AtomPix.Imaging.Abstractions.Formats;
using AtomPix.Imaging.Magick.DependencyInjection;
using AtomPix.Imaging.Magick.Processing;

public sealed class MagickUnitTests
{
    [Fact]
    public void Processor_declares_first_release_capabilities()
    {
        var processor = new MagickImageProcessor();

        Assert.Contains(ImageFormatKind.Jpeg, processor.Capabilities.SupportedInputFormats);
        Assert.Contains(ImageFormatKind.Png, processor.Capabilities.SupportedInputFormats);
        Assert.Contains(ImageFormatKind.WebP, processor.Capabilities.SupportedInputFormats);
        Assert.Contains(OutputImageFormat.Jpeg, processor.Capabilities.SupportedOutputFormats);
        Assert.Contains(OutputImageFormat.Png, processor.Capabilities.SupportedOutputFormats);
        Assert.Contains(OutputImageFormat.WebP, processor.Capabilities.SupportedOutputFormats);
        Assert.True(processor.Capabilities.SupportsMetadata);
        Assert.False(processor.Capabilities.SupportsAnimatedImages);
    }

    [Fact]
    public void Dependency_injection_extension_rejects_null_services()
    {
        Assert.Throws<ArgumentNullException>(() => MagickServiceCollectionExtensions.AddAtomPixMagickImaging(null!));
    }
}
