namespace AtomPix.Imaging.Magick.Tests;

using AtomPix.Core.Conversion;
using AtomPix.Imaging.Abstractions.Formats;
using AtomPix.Imaging.Abstractions.Processing;
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

    [Fact]
    public void Processor_options_drive_public_resource_capabilities()
    {
        var resources = new ImageResourceCapabilities(10, 20, 30, 400, 50, 60, 700);
        var options = new MagickImageProcessorOptions(resources, 100, 200, 300, 1, Path.GetTempPath());

        var processor = new MagickImageProcessor(options);

        Assert.Same(resources, processor.Capabilities.Resources);
        Assert.Equal(50, processor.Capabilities.Resize!.MaxWidth);
        Assert.Equal(20, processor.Capabilities.Crop!.MaxInputWidth);
    }
}
