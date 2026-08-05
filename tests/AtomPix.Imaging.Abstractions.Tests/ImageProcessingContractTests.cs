namespace AtomPix.Imaging.Abstractions.Tests;

using AtomPix.Core.Compression;
using AtomPix.Core.Conversion;
using AtomPix.Core.ValueObjects;
using AtomPix.Imaging.Abstractions.Formats;
using AtomPix.Imaging.Abstractions.Processing;

public sealed class ImageProcessingContractTests
{
    private static readonly LocalPath Input = new("C:\\images\\input.jpg");
    private static readonly LocalPath Output = new("C:\\images\\output.webp");

    [Fact]
    public void Capabilities_require_declared_formats()
    {
        Assert.Throws<ArgumentException>(() => new ImageProcessorCapabilities(
            new HashSet<ImageFormatKind>(),
            new HashSet<OutputImageFormat> { OutputImageFormat.Jpeg },
            supportsMetadata: true,
            supportsAnimatedImages: false));

        Assert.Throws<ArgumentException>(() => new ImageProcessorCapabilities(
            new HashSet<ImageFormatKind> { ImageFormatKind.Unknown },
            new HashSet<OutputImageFormat> { OutputImageFormat.Jpeg },
            supportsMetadata: true,
            supportsAnimatedImages: false));
    }

    [Fact]
    public void Capabilities_copy_format_sets()
    {
        var inputs = new HashSet<ImageFormatKind> { ImageFormatKind.Jpeg };
        var outputs = new HashSet<OutputImageFormat> { OutputImageFormat.Jpeg };

        var capabilities = new ImageProcessorCapabilities(inputs, outputs, true, false);
        inputs.Add(ImageFormatKind.Png);
        outputs.Add(OutputImageFormat.WebP);

        Assert.DoesNotContain(ImageFormatKind.Png, capabilities.SupportedInputFormats);
        Assert.DoesNotContain(OutputImageFormat.WebP, capabilities.SupportedOutputFormats);
    }


    [Fact]
    public void Capabilities_reject_null_format_sets()
    {
        Assert.Throws<ArgumentNullException>(() => new ImageProcessorCapabilities(
            null!,
            new HashSet<OutputImageFormat> { OutputImageFormat.Jpeg },
            supportsMetadata: true,
            supportsAnimatedImages: false));

        Assert.Throws<ArgumentNullException>(() => new ImageProcessorCapabilities(
            new HashSet<ImageFormatKind> { ImageFormatKind.Jpeg },
            null!,
            supportsMetadata: true,
            supportsAnimatedImages: false));
    }

    [Fact]
    public void Preview_result_exposes_defensive_copy()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var result = new ImagePreviewResult(bytes, "image/png", 10, 10);

        result.EncodedBytes[0] = 9;

        Assert.Equal(1, result.EncodedBytes[0]);
    }
    [Fact]
    public void Probe_result_requires_valid_dimensions_size_and_format()
    {
        Assert.Throws<ArgumentException>(() => new ImageProbeResult(Input, ImageFormatKind.Unknown, 100, 100, 1, false, false, 1, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageProbeResult(Input, ImageFormatKind.Jpeg, 0, 100, 1, false, false, 1, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageProbeResult(Input, ImageFormatKind.Jpeg, 100, 100, -1, false, false, 1, false));
        Assert.Throws<ArgumentException>(() => new ImageProbeResult(Input, ImageFormatKind.Gif, 100, 100, 1, false, true, 1, false));
    }

    [Fact]
    public void Preview_request_requires_positive_max_pixel_size()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImagePreviewRequest(Input, 0));
    }

    [Fact]
    public void Preview_result_copies_encoded_bytes()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var result = new ImagePreviewResult(bytes, "image/jpeg", 10, 10);

        bytes[0] = 9;

        Assert.Equal(1, result.EncodedBytes[0]);
    }

    [Fact]
    public void Preview_result_requires_payload_mime_and_dimensions()
    {
        Assert.Throws<ArgumentException>(() => new ImagePreviewResult(Array.Empty<byte>(), "image/jpeg", 10, 10));
        Assert.Throws<ArgumentException>(() => new ImagePreviewResult([1], " ", 10, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImagePreviewResult([1], "image/jpeg", 0, 10));
    }

    [Fact]
    public void Processing_requests_require_profiles()
    {
        Assert.Throws<ArgumentNullException>(() => new ImageCompressRequest(Input, Output, null!));
        Assert.Throws<ArgumentNullException>(() => new ImageConvertRequest(Input, Output, null!));
    }

    [Fact]
    public void Processing_results_require_known_formats_and_non_negative_sizes()
    {
        Assert.Throws<ArgumentException>(() => new ImageCompressResult(Input, Output, ImageFormatKind.Unknown, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageCompressResult(Input, Output, ImageFormatKind.Jpeg, -1, 1));
        Assert.Throws<ArgumentException>(() => new ImageConvertResult(Input, Output, ImageFormatKind.Unknown, ImageFormatKind.WebP, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageConvertResult(Input, Output, ImageFormatKind.Jpeg, ImageFormatKind.WebP, 1, -1));
    }


    [Fact]
    public void Processing_details_require_positive_dimensions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageProcessingDetails(0, 10, 10, 10, false, false, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageProcessingDetails(10, 10, 0, 10, false, false, false));
    }

    [Fact]
    public void Processing_results_can_carry_details()
    {
        var details = new ImageProcessingDetails(120, 80, 60, 40, resizeApplied: true, metadataRemoved: true, lossyOutput: true);

        var compress = new ImageCompressResult(Input, Output, ImageFormatKind.Jpeg, 100, 70, details);
        var convert = new ImageConvertResult(Input, Output, ImageFormatKind.Png, ImageFormatKind.WebP, 100, 60, details);

        Assert.Same(details, compress.Details);
        Assert.Same(details, convert.Details);
        Assert.True(compress.Details!.ResizeApplied);
        Assert.True(convert.Details!.LossyOutput);
    }
    [Fact]
    public void Valid_requests_and_results_can_be_created()
    {
        var compress = new ImageCompressRequest(Input, Output, CompressionProfile.BalancedDefault());
        var convert = new ImageConvertRequest(Input, Output, ConversionProfile.WebPDefault());
        var probe = new ImageProbeResult(Input, ImageFormatKind.Jpeg, 100, 50, 1024, false, false, 1, true);

        Assert.Equal(Input, compress.InputPath);
        Assert.Equal(Output, convert.OutputPath);
        Assert.Equal(ImageFormatKind.Jpeg, probe.Format);
    }
}

