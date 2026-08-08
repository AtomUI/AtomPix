namespace AtomPix.Imaging.Abstractions.Tests;

using AtomPix.Core.Compression;
using AtomPix.Core.Conversion;
using AtomPix.Core.Crop;
using AtomPix.Core.Resize;
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
            supportsAnimatedImages: false,
            Resources(),
            null,
            null));

        Assert.Throws<ArgumentException>(() => new ImageProcessorCapabilities(
            new HashSet<ImageFormatKind> { ImageFormatKind.Unknown },
            new HashSet<OutputImageFormat> { OutputImageFormat.Jpeg },
            supportsMetadata: true,
            supportsAnimatedImages: false,
            Resources(),
            null,
            null));
    }

    [Fact]
    public void Capabilities_copy_format_sets()
    {
        var inputs = new HashSet<ImageFormatKind> { ImageFormatKind.Jpeg };
        var outputs = new HashSet<OutputImageFormat> { OutputImageFormat.Jpeg };

        var resizeFormats = new HashSet<ImageFormatKind> { ImageFormatKind.Jpeg };
        var capabilities = new ImageProcessorCapabilities(
            inputs,
            outputs,
            true,
            false,
            Resources(),
            new ImageResizeCapabilities(resizeFormats, 1000, 1000, 1_000_000),
            null);
        inputs.Add(ImageFormatKind.Png);
        outputs.Add(OutputImageFormat.WebP);
        resizeFormats.Add(ImageFormatKind.Png);

        Assert.DoesNotContain(ImageFormatKind.Png, capabilities.SupportedInputFormats);
        Assert.DoesNotContain(OutputImageFormat.WebP, capabilities.SupportedOutputFormats);
        Assert.DoesNotContain(ImageFormatKind.Png, capabilities.Resize!.SupportedSameFormatFormats);
    }


    [Fact]
    public void Capabilities_reject_null_format_sets()
    {
        Assert.Throws<ArgumentNullException>(() => new ImageProcessorCapabilities(
            null!,
            new HashSet<OutputImageFormat> { OutputImageFormat.Jpeg },
            supportsMetadata: true,
            supportsAnimatedImages: false,
            Resources(),
            null,
            null));

        Assert.Throws<ArgumentNullException>(() => new ImageProcessorCapabilities(
            new HashSet<ImageFormatKind> { ImageFormatKind.Jpeg },
            null!,
            supportsMetadata: true,
            supportsAnimatedImages: false,
            Resources(),
            null,
            null));
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
        Assert.Throws<ArgumentException>(() => new ImageProbeResult(Input, ImageFormatKind.Unknown, 100, 100, 1, false, false, false, 1, false, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageProbeResult(Input, ImageFormatKind.Jpeg, 0, 100, 1, false, false, false, 1, false, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageProbeResult(Input, ImageFormatKind.Jpeg, 100, 100, -1, false, false, false, 1, false, false));
        Assert.Throws<ArgumentException>(() => new ImageProbeResult(Input, ImageFormatKind.Gif, 100, 100, 1, false, false, true, 1, false, false));
        Assert.Throws<ArgumentException>(() => new ImageProbeResult(Input, ImageFormatKind.Png, 100, 100, 1, false, true, false, 1, false, false));
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
        Assert.Throws<ArgumentNullException>(() => new ImageResizeRequest(Input, Output, null!, SameFormatEncodingPolicy.Default));
        Assert.Throws<ArgumentNullException>(() => new ImageCropRequest(Input, Output, null!, SameFormatEncodingPolicy.Default));
    }

    [Fact]
    public void Processing_results_require_known_formats_and_non_negative_sizes()
    {
        Assert.Throws<ArgumentException>(() => new ImageCompressResult(Input, Output, ImageFormatKind.Unknown, ImageFormatKind.Unknown, 1, 1, null));
        Assert.Throws<ArgumentException>(() => new ImageCompressResult(Input, Output, ImageFormatKind.Jpeg, ImageFormatKind.WebP, 1, 1, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageCompressResult(Input, Output, ImageFormatKind.Jpeg, ImageFormatKind.Jpeg, -1, 1, new ImageQuality(80)));
        Assert.Throws<ArgumentException>(() => new ImageConvertResult(Input, Output, ImageFormatKind.Unknown, ImageFormatKind.WebP, 1, 1, NotPresent()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageConvertResult(Input, Output, ImageFormatKind.Jpeg, ImageFormatKind.WebP, 1, -1, NotPresent()));
    }


    [Fact]
    public void Processing_details_require_positive_dimensions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageProcessingDetails(0, 10, 10, 10, false, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageProcessingDetails(10, 10, 0, 10, false, false));
        Assert.Throws<ArgumentException>(() => new ImageProcessingDetails(10, 10, 5, 5, false, false));
    }

    [Fact]
    public void Processing_results_can_carry_details()
    {
        var details = new ImageProcessingDetails(120, 80, 120, 80, metadataRemoved: true, lossyOutput: true);

        var compress = new ImageCompressResult(Input, Output, ImageFormatKind.Jpeg, ImageFormatKind.Jpeg, 100, 70, new ImageQuality(80), details);
        var convert = new ImageConvertResult(Input, Output, ImageFormatKind.Png, ImageFormatKind.WebP, 100, 60, new TransparencyProcessingResult(TransparencyOutcome.Preserved, null), details);

        Assert.Same(details, compress.Details);
        Assert.Same(details, convert.Details);
        Assert.Equal(80, compress.AppliedQuality?.Value);
        Assert.True(convert.Details!.LossyOutput);
    }
    [Fact]
    public void Valid_requests_and_results_can_be_created()
    {
        var compress = new ImageCompressRequest(Input, Output, CompressionProfile.BalancedDefault());
        var convert = new ImageConvertRequest(Input, Output, ConversionProfile.WebPDefault());
        var probe = new ImageProbeResult(Input, ImageFormatKind.Jpeg, 100, 50, 1024, false, false, false, 1, true, false);
        var resize = new ImageResizeRequest(Input, new LocalPath("output.jpg"), new ResolvedResizeSize(50, 25), SameFormatEncodingPolicy.Default);
        var crop = new ImageCropRequest(Input, new LocalPath("output.jpg"), new CropRectangle(0, 0, 50, 25), SameFormatEncodingPolicy.Default);

        Assert.Equal(Input, compress.InputPath);
        Assert.Equal(Output, convert.OutputPath);
        Assert.Equal(ImageFormatKind.Jpeg, probe.Format);
        Assert.Equal(50, resize.TargetSize.Width);
        Assert.Equal(25, crop.CropArea.Height);
    }

    [Fact]
    public void Transparency_results_enforce_outcome_shape()
    {
        Assert.Throws<ArgumentNullException>(() => new TransparencyProcessingResult(TransparencyOutcome.Flattened, null));
        Assert.Throws<ArgumentException>(() => new TransparencyProcessingResult(TransparencyOutcome.Preserved, RgbColor.White));
        Assert.Equal(RgbColor.White, new TransparencyProcessingResult(TransparencyOutcome.Flattened, RgbColor.White).BackgroundColor);
    }

    private static ImageResourceCapabilities Resources() => new(1024, 1000, 1000, 1_000_000, 1000, 1000, 1_000_000);

    private static TransparencyProcessingResult NotPresent() => new(TransparencyOutcome.NotPresent, null);
}

