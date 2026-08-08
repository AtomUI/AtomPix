namespace AtomPix.Imaging.Magick.Tests;

using ImageMagick;
using AtomPix.Core.Compression;
using AtomPix.Core.Conversion;
using AtomPix.Core.Crop;
using AtomPix.Core.Errors;
using AtomPix.Core.Resize;
using AtomPix.Core.ValueObjects;
using AtomPix.Imaging.Abstractions.Formats;
using AtomPix.Imaging.Abstractions.Processing;
using AtomPix.Imaging.Magick.Processing;

public sealed class MagickImageProcessorTests : IDisposable
{
    private readonly string _root;
    private readonly MagickImageProcessor _processor = new();

    public MagickImageProcessorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "AtomPixMagickTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        CreateSampleImages();
    }

    [Theory]
    [InlineData("jpeg-basic.jpg", ImageFormatKind.Jpeg)]
    [InlineData("png-alpha.png", ImageFormatKind.Png)]
    [InlineData("webp-basic.webp", ImageFormatKind.WebP)]
    [InlineData("bmp-basic.bmp", ImageFormatKind.Bmp)]
    [InlineData("tiff-basic.tiff", ImageFormatKind.Tiff)]
    public async Task Probe_reads_supported_formats(string fileName, ImageFormatKind expectedFormat)
    {
        var result = await _processor.ProbeAsync(new ImageProbeRequest(PathOf(fileName)), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(expectedFormat, result.Value!.Format);
        Assert.True(result.Value.Width > 0);
        Assert.True(result.Value.FileSizeBytes > 0);
    }

    [Fact]
    public async Task Probe_rejects_file_size_limit_before_image_header_read()
    {
        var processor = CreateLimitedProcessor(new ImageResourceCapabilities(1, 1000, 1000, 1_000_000, 1000, 1000, 1_000_000));

        var result = await processor.ProbeAsync(new ImageProbeRequest(PathOf("jpeg-basic.jpg")), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AtomPixErrorCode.InputFileTooLarge, result.Error!.Code);
        Assert.Equal("InputFileSizeBytes", result.Error.Details!["ResourceKind"]);
    }

    [Fact]
    public async Task Probe_rejects_dimension_limit_from_lightweight_header()
    {
        var processor = CreateLimitedProcessor(new ImageResourceCapabilities(1024 * 1024, 100, 100, 10_000, 100, 100, 10_000));

        var result = await processor.ProbeAsync(new ImageProbeRequest(PathOf("jpeg-basic.jpg")), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AtomPixErrorCode.ImageDimensionsExceedLimit, result.Error!.Code);
        Assert.Equal("120", result.Error.Details!["ActualWidth"]);
        Assert.Equal("100", result.Error.Details["MaximumWidth"]);
    }

    [Fact]
    public async Task Capabilities_match_probe_and_convert_behavior()
    {
        var samples = new Dictionary<ImageFormatKind, string>
        {
            [ImageFormatKind.Jpeg] = "jpeg-basic.jpg",
            [ImageFormatKind.Png] = "png-alpha.png",
            [ImageFormatKind.WebP] = "webp-basic.webp",
            [ImageFormatKind.Bmp] = "bmp-basic.bmp",
            [ImageFormatKind.Gif] = "gif-animated.gif",
            [ImageFormatKind.Tiff] = "tiff-basic.tiff"
        };

        foreach (var inputFormat in _processor.Capabilities.SupportedInputFormats)
        {
            var probe = await _processor.ProbeAsync(new ImageProbeRequest(PathOf(samples[inputFormat])), CancellationToken.None);

            Assert.True(probe.Succeeded, $"Probe should support {inputFormat}.");
            Assert.Equal(inputFormat, probe.Value!.Format);
        }

        foreach (var outputFormat in _processor.Capabilities.SupportedOutputFormats)
        {
            var extension = outputFormat switch
            {
                OutputImageFormat.Jpeg => "jpg",
                OutputImageFormat.Png => "png",
                OutputImageFormat.WebP => "webp",
                _ => throw new InvalidOperationException($"Unexpected output format {outputFormat}.")
            };
            var output = PathOf($"capability-convert.{extension}");
            var profile = new ConversionProfile(outputFormat, new ImageQuality(80), MetadataPolicy.Remove, TransparencyPolicy.Default);

            var convert = await _processor.ConvertAsync(new ImageConvertRequest(PathOf("jpeg-basic.jpg"), output, profile), CancellationToken.None);

            Assert.True(convert.Succeeded, $"Convert should support {outputFormat}.");
            Assert.Equal(ToImageFormatKind(outputFormat), convert.Value!.OutputFormat);
            Assert.True(File.Exists(output.Value));
        }
    }

    [Fact]
    public async Task Probe_detects_png_alpha()
    {
        var result = await _processor.ProbeAsync(new ImageProbeRequest(PathOf("png-alpha.png")), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.Value!.HasAlphaChannel);
        Assert.True(result.Value.HasTransparency);
    }

    [Fact]
    public async Task Probe_detects_animated_gif()
    {
        var result = await _processor.ProbeAsync(new ImageProbeRequest(PathOf("gif-animated.gif")), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(ImageFormatKind.Gif, result.Value!.Format);
        Assert.True(result.Value.IsAnimated);
        Assert.True(result.Value.FrameCount > 1);
    }

    [Fact]
    public async Task Preview_uses_png_for_alpha_images()
    {
        var result = await _processor.CreatePreviewAsync(new ImagePreviewRequest(PathOf("png-alpha.png"), 64), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("image/png", result.Value!.MimeType);
        Assert.NotEmpty(result.Value.EncodedBytes);
    }

    [Fact]
    public async Task Preview_uses_jpeg_for_non_alpha_images()
    {
        var result = await _processor.CreatePreviewAsync(new ImagePreviewRequest(PathOf("jpeg-basic.jpg"), 64), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("image/jpeg", result.Value!.MimeType);
        Assert.NotEmpty(result.Value.EncodedBytes);
    }

    [Fact]
    public async Task Compress_jpeg_balanced_writes_output()
    {
        var output = PathOf("compressed.jpg");
        var result = await _processor.CompressAsync(
            new ImageCompressRequest(PathOf("jpeg-basic.jpg"), output, CompressionProfile.BalancedDefault()),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(File.Exists(output.Value));
        Assert.Equal(ImageFormatKind.Jpeg, result.Value!.OutputFormat);
    }

    [Fact]
    public async Task Convert_png_to_webp_writes_output()
    {
        var output = PathOf("converted.webp");
        var result = await _processor.ConvertAsync(
            new ImageConvertRequest(PathOf("png-alpha.png"), output, ConversionProfile.WebPDefault()),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(File.Exists(output.Value));
        Assert.Equal(ImageFormatKind.Png, result.Value!.InputFormat);
        Assert.Equal(ImageFormatKind.WebP, result.Value.OutputFormat);
    }

    [Fact]
    public async Task Convert_webp_to_jpeg_writes_output()
    {
        var output = PathOf("converted.jpg");
        var profile = new ConversionProfile(OutputImageFormat.Jpeg, new ImageQuality(80), MetadataPolicy.Remove, TransparencyPolicy.Default);

        var result = await _processor.ConvertAsync(new ImageConvertRequest(PathOf("webp-basic.webp"), output, profile), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(File.Exists(output.Value));
        Assert.Equal(ImageFormatKind.Jpeg, result.Value!.OutputFormat);
    }

    [Fact]
    public async Task Compress_rejects_multi_frame_images()
    {
        var output = PathOf("animated-output.gif");

        var result = await _processor.CompressAsync(
            new ImageCompressRequest(PathOf("gif-animated.gif"), output, CompressionProfile.BalancedDefault()),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AtomPixErrorCode.UnsupportedInputFormat, result.Error!.Code);
    }

    [Fact]
    public async Task Convert_rejects_multi_frame_images()
    {
        var output = PathOf("animated-output.webp");

        var result = await _processor.ConvertAsync(
            new ImageConvertRequest(PathOf("gif-animated.gif"), output, ConversionProfile.WebPDefault()),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AtomPixErrorCode.UnsupportedInputFormat, result.Error!.Code);
        Assert.False(File.Exists(output.Value));
    }

    [Fact]
    public async Task Missing_file_maps_to_input_file_not_found()
    {
        var result = await _processor.ProbeAsync(new ImageProbeRequest(PathOf("missing.jpg")), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AtomPixErrorCode.InputFileNotFound, result.Error!.Code);
    }

    [Fact]
    public async Task Preview_missing_file_maps_to_input_file_not_found()
    {
        var result = await _processor.CreatePreviewAsync(new ImagePreviewRequest(PathOf("missing.jpg"), 64), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AtomPixErrorCode.InputFileNotFound, result.Error!.Code);
    }

    [Fact]
    public async Task Compress_missing_file_maps_to_input_file_not_found()
    {
        var result = await _processor.CompressAsync(
            new ImageCompressRequest(PathOf("missing.jpg"), PathOf("missing-output.jpg"), CompressionProfile.BalancedDefault()),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AtomPixErrorCode.InputFileNotFound, result.Error!.Code);
    }

    [Fact]
    public async Task Convert_missing_file_maps_to_input_file_not_found()
    {
        var result = await _processor.ConvertAsync(
            new ImageConvertRequest(PathOf("missing.jpg"), PathOf("missing-output.webp"), ConversionProfile.WebPDefault()),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AtomPixErrorCode.InputFileNotFound, result.Error!.Code);
    }



    [Fact]
    public async Task Probe_non_image_file_maps_to_invalid_image_file()
    {
        var result = await _processor.ProbeAsync(new ImageProbeRequest(PathOf("not-image.txt")), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AtomPixErrorCode.InvalidImageFile, result.Error!.Code);
    }

    [Fact]
    public async Task Preview_corrupt_image_maps_to_invalid_image_file()
    {
        var result = await _processor.CreatePreviewAsync(new ImagePreviewRequest(PathOf("corrupt.jpg"), 64), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AtomPixErrorCode.InvalidImageFile, result.Error!.Code);
    }

    [Fact]
    public async Task Compress_corrupt_image_maps_to_compress_failure()
    {
        var output = PathOf("corrupt-output.jpg");

        var result = await _processor.CompressAsync(
            new ImageCompressRequest(PathOf("corrupt.jpg"), output, CompressionProfile.BalancedDefault()),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AtomPixErrorCode.ImageCompressFailed, result.Error!.Code);
    }

    [Fact]
    public async Task Convert_corrupt_image_maps_to_convert_failure()
    {
        var output = PathOf("corrupt-output.webp");

        var result = await _processor.ConvertAsync(
            new ImageConvertRequest(PathOf("corrupt.jpg"), output, ConversionProfile.WebPDefault()),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AtomPixErrorCode.ImageConvertFailed, result.Error!.Code);
    }
    [Fact]
    public async Task Compress_rejects_output_format_outside_declared_capabilities()
    {
        var output = PathOf("compressed.bmp");

        var result = await _processor.CompressAsync(
            new ImageCompressRequest(PathOf("jpeg-basic.jpg"), output, CompressionProfile.BalancedDefault()),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AtomPixErrorCode.UnsupportedOutputFormat, result.Error!.Code);
    }



    [Fact]
    public async Task Compress_writes_file_name_only_output_path()
    {
        var previousDirectory = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(_root);
            var output = new LocalPath("filename-only-output.jpg");

            var result = await _processor.CompressAsync(
                new ImageCompressRequest(PathOf("jpeg-basic.jpg"), output, CompressionProfile.BalancedDefault()),
                CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.True(File.Exists(Path.Combine(_root, output.Value)));
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
        }
    }

    [Fact]
    public async Task Convert_rejects_missing_output_directory_without_creating_it()
    {
        var output = new LocalPath(Path.Combine(_root, "nested", "directory", "converted.webp"));

        var result = await _processor.ConvertAsync(
            new ImageConvertRequest(PathOf("png-alpha.png"), output, ConversionProfile.WebPDefault()),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AtomPixErrorCode.OutputDirectoryNotFound, result.Error!.Code);
        Assert.False(Directory.Exists(Path.Combine(_root, "nested")));
    }
    [Fact]
    public async Task Compress_success_does_not_leave_temporary_output_files()
    {
        var output = PathOf("compressed-temp-clean.jpg");

        var result = await _processor.CompressAsync(
            new ImageCompressRequest(PathOf("jpeg-basic.jpg"), output, CompressionProfile.BalancedDefault()),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Empty(TemporaryFilesIn(_root));
    }

    [Fact]
    public async Task Convert_success_does_not_leave_temporary_output_files()
    {
        var output = PathOf("converted-temp-clean.webp");

        var result = await _processor.ConvertAsync(
            new ImageConvertRequest(PathOf("png-alpha.png"), output, ConversionProfile.WebPDefault()),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Empty(TemporaryFilesIn(_root));
    }

    [Fact]
    public async Task Compress_write_failure_cleans_temporary_output_file()
    {
        var outputDirectory = Path.Combine(_root, "directory-as-output.jpg");
        Directory.CreateDirectory(outputDirectory);

        var result = await _processor.CompressAsync(
            new ImageCompressRequest(PathOf("jpeg-basic.jpg"), new LocalPath(outputDirectory), CompressionProfile.BalancedDefault()),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AtomPixErrorCode.ImageWriteFailed, result.Error!.Code);
        Assert.Empty(TemporaryFilesIn(_root));
    }

    [Fact]
    public void Atomic_output_commit_preserves_existing_file_and_cleans_temporary_file_when_encoding_fails()
    {
        var output = Path.Combine(_root, "existing-output.jpg");
        File.WriteAllText(output, "original");
        var committer = new AtomicImageFileCommitter();

        var exception = Assert.Throws<ImageOutputCommitException>(() =>
            committer.Commit(
                new LocalPath(output),
                temporaryPath =>
                {
                    File.WriteAllText(temporaryPath, "partial");
                    throw new IOException("Synthetic encoder failure.");
                }));

        Assert.Equal(ImageOutputFailureKind.WriteFailed, exception.Kind);
        Assert.Equal("original", File.ReadAllText(output));
        Assert.Empty(TemporaryFilesIn(_root));
    }

    [Fact]
    public void Atomic_output_commit_classifies_native_disk_full_error()
    {
        var committer = new AtomicImageFileCommitter();

        var exception = Assert.Throws<ImageOutputCommitException>(() =>
            committer.Commit(
                PathOf("disk-full.jpg"),
                _ => throw new IOException("Synthetic disk full.", 112)));

        Assert.Equal(ImageOutputFailureKind.InsufficientDiskSpace, exception.Kind);
        Assert.Empty(TemporaryFilesIn(_root));
    }

    [Fact]
    public void Atomic_output_commit_classifies_permission_denied_error()
    {
        var committer = new AtomicImageFileCommitter();

        var exception = Assert.Throws<ImageOutputCommitException>(() =>
            committer.Commit(
                PathOf("permission-denied.jpg"),
                _ => throw new UnauthorizedAccessException("Synthetic permission denial.")));

        Assert.Equal(ImageOutputFailureKind.PermissionDenied, exception.Kind);
        Assert.Empty(TemporaryFilesIn(_root));
    }

    [Theory]
    [InlineData(ImageOutputFailureKind.InsufficientDiskSpace, AtomPixErrorCode.InsufficientDiskSpace, AtomPixErrorCategory.FileSystem)]
    [InlineData(ImageOutputFailureKind.PermissionDenied, AtomPixErrorCode.ImageWriteFailed, AtomPixErrorCategory.Permission)]
    [InlineData(ImageOutputFailureKind.WriteFailed, AtomPixErrorCode.ImageWriteFailed, AtomPixErrorCategory.FileSystem)]
    public async Task Convert_maps_output_commit_failures_to_stable_public_errors(
        ImageOutputFailureKind failureKind,
        AtomPixErrorCode expectedCode,
        AtomPixErrorCategory expectedCategory)
    {
        var output = PathOf($"failed-{failureKind}.webp");
        var processor = new MagickImageProcessor(
            MagickImageProcessorOptions.CreateDefault(Path.Combine(_root, "fault-cache")),
            fileCommitter: new ThrowingImageFileCommitter(failureKind));

        var result = await processor.ConvertAsync(
            new ImageConvertRequest(PathOf("png-alpha.png"), output, ConversionProfile.WebPDefault()),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(expectedCode, result.Error!.Code);
        Assert.Equal(expectedCategory, result.Error.Category);
        Assert.False(File.Exists(output.Value));
    }
    [Fact]
    public void Convert_profile_rejects_invalid_output_format_enum_before_processor_call()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ConversionProfile((OutputImageFormat)999, null, MetadataPolicy.Remove, TransparencyPolicy.Default));
    }

    [Fact]
    public async Task Operations_map_canceled_token_to_canceled_failure()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var probe = await _processor.ProbeAsync(new ImageProbeRequest(PathOf("jpeg-basic.jpg")), cts.Token);
        var preview = await _processor.CreatePreviewAsync(new ImagePreviewRequest(PathOf("jpeg-basic.jpg"), 64), cts.Token);
        var compress = await _processor.CompressAsync(
            new ImageCompressRequest(PathOf("jpeg-basic.jpg"), PathOf("canceled-output.jpg"), CompressionProfile.BalancedDefault()),
            cts.Token);
        var convert = await _processor.ConvertAsync(
            new ImageConvertRequest(PathOf("png-alpha.png"), PathOf("canceled-output.webp"), ConversionProfile.WebPDefault()),
            cts.Token);
        var resize = await _processor.ResizeAsync(
            new ImageResizeRequest(PathOf("jpeg-basic.jpg"), PathOf("canceled-resize.jpg"), new ResolvedResizeSize(60, 40), SameFormatEncodingPolicy.Default),
            cts.Token);
        var crop = await _processor.CropAsync(
            new ImageCropRequest(PathOf("jpeg-basic.jpg"), PathOf("canceled-crop.jpg"), new CropRectangle(0, 0, 60, 40), SameFormatEncodingPolicy.Default),
            cts.Token);

        Assert.False(probe.Succeeded);
        Assert.Equal(AtomPixErrorCode.OperationCanceled, probe.Error!.Code);
        Assert.False(preview.Succeeded);
        Assert.Equal(AtomPixErrorCode.OperationCanceled, preview.Error!.Code);
        Assert.False(compress.Succeeded);
        Assert.Equal(AtomPixErrorCode.OperationCanceled, compress.Error!.Code);
        Assert.False(convert.Succeeded);
        Assert.Equal(AtomPixErrorCode.OperationCanceled, convert.Error!.Code);
        Assert.False(resize.Succeeded);
        Assert.Equal(AtomPixErrorCode.OperationCanceled, resize.Error!.Code);
        Assert.False(crop.Succeeded);
        Assert.Equal(AtomPixErrorCode.OperationCanceled, crop.Error!.Code);
    }

    [Fact]
    public async Task Operations_reject_null_requests_as_programmer_error()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _processor.ProbeAsync(null!, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _processor.CreatePreviewAsync(null!, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _processor.CompressAsync(null!, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _processor.ConvertAsync(null!, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _processor.ResizeAsync(null!, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _processor.CropAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task Compress_jpeg_quality_profiles_produce_distinct_sizes()
    {
        var highQualityOutput = PathOf("compressed-high-quality.jpg");
        var maximumOutput = PathOf("compressed-maximum-quality.jpg");

        var highQuality = await _processor.CompressAsync(
            new ImageCompressRequest(PathOf("jpeg-detailed.jpg"), highQualityOutput, CompressionProfile.HighQualityDefault()),
            CancellationToken.None);
        var maximum = await _processor.CompressAsync(
            new ImageCompressRequest(PathOf("jpeg-detailed.jpg"), maximumOutput, CompressionProfile.MaximumDefault()),
            CancellationToken.None);

        Assert.True(highQuality.Succeeded);
        Assert.True(maximum.Succeeded);
        Assert.True(highQuality.Value!.OutputSizeBytes > maximum.Value!.OutputSizeBytes);
    }

    [Fact]
    public async Task Compress_metadata_policy_removes_or_preserves_metadata()
    {
        var removeOutput = PathOf("metadata-removed.jpg");
        var preserveOutput = PathOf("metadata-preserved.jpg");
        var removeProfile = new CompressionProfile(CompressionMode.Balanced, new ImageQuality(80), MetadataPolicy.Remove);
        var preserveProfile = new CompressionProfile(CompressionMode.Balanced, new ImageQuality(80), MetadataPolicy.Preserve);

        var removed = await _processor.CompressAsync(new ImageCompressRequest(PathOf("jpeg-metadata.jpg"), removeOutput, removeProfile), CancellationToken.None);
        var preserved = await _processor.CompressAsync(new ImageCompressRequest(PathOf("jpeg-metadata.jpg"), preserveOutput, preserveProfile), CancellationToken.None);

        Assert.True(removed.Succeeded);
        Assert.True(preserved.Succeeded);
        using var removedImage = new MagickImage(removeOutput.Value);
        using var preservedImage = new MagickImage(preserveOutput.Value);
        Assert.Null(removedImage.GetExifProfile());
        Assert.NotNull(preservedImage.GetExifProfile());
        Assert.True(removed.Value!.Details!.MetadataRemoved);
        Assert.False(preserved.Value!.Details!.MetadataRemoved);
    }

    [Fact]
    public async Task Compress_jpeg_maximum_reduces_file_size_and_preserves_dimensions()
    {
        var input = PathOf("jpeg-detailed.jpg");
        var output = PathOf("compressed-maximum.jpg");
        var inputSize = new FileInfo(input.Value).Length;

        var result = await _processor.CompressAsync(
            new ImageCompressRequest(input, output, CompressionProfile.MaximumDefault()),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.Value!.OutputSizeBytes < inputSize);
        using var compressed = new MagickImage(output.Value);
        Assert.Equal(320u, compressed.Width);
        Assert.Equal(240u, compressed.Height);
    }

    [Fact]
    public async Task Convert_png_alpha_to_webp_preserves_dimensions_alpha_and_writes_webp()
    {
        var output = PathOf("alpha-converted.webp");

        var result = await _processor.ConvertAsync(
            new ImageConvertRequest(PathOf("png-alpha.png"), output, ConversionProfile.WebPDefault()),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(ImageFormatKind.WebP, result.Value!.OutputFormat);
        using var converted = new MagickImage(output.Value);
        Assert.Equal(MagickFormat.WebP, converted.Format);
        Assert.Equal(120u, converted.Width);
        Assert.Equal(80u, converted.Height);
        Assert.True(converted.HasAlpha);
        Assert.Equal(TransparencyOutcome.Preserved, result.Value.Transparency.Outcome);
    }

    [Fact]
    public async Task Convert_png_alpha_to_jpeg_removes_alpha()
    {
        var output = PathOf("alpha-converted.jpg");
        var profile = new ConversionProfile(OutputImageFormat.Jpeg, new ImageQuality(80), MetadataPolicy.Remove, TransparencyPolicy.Default);

        var result = await _processor.ConvertAsync(new ImageConvertRequest(PathOf("png-alpha.png"), output, profile), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(ImageFormatKind.Jpeg, result.Value!.OutputFormat);
        using var converted = new MagickImage(output.Value);
        Assert.Equal(MagickFormat.Jpeg, converted.Format);
        Assert.False(converted.HasAlpha);
        Assert.Equal(TransparencyOutcome.Flattened, result.Value.Transparency.Outcome);
        Assert.Equal(RgbColor.White, result.Value.Transparency.BackgroundColor);
    }

    [Fact]
    public async Task Convert_webp_to_jpeg_outputs_jpeg_without_alpha()
    {
        var output = PathOf("webp-to-jpeg.jpg");
        var profile = new ConversionProfile(OutputImageFormat.Jpeg, new ImageQuality(80), MetadataPolicy.Remove, TransparencyPolicy.Default);

        var result = await _processor.ConvertAsync(new ImageConvertRequest(PathOf("webp-basic.webp"), output, profile), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(ImageFormatKind.Jpeg, result.Value!.OutputFormat);
        using var converted = new MagickImage(output.Value);
        Assert.Equal(MagickFormat.Jpeg, converted.Format);
        Assert.False(converted.HasAlpha);
    }

    [Fact]
    public async Task Resize_executes_resolved_dimensions_without_reinterpreting_aspect_ratio()
    {
        var output = PathOf("resized.jpg");

        var result = await _processor.ResizeAsync(
            new ImageResizeRequest(PathOf("jpeg-basic.jpg"), output, new ResolvedResizeSize(60, 30), SameFormatEncodingPolicy.Default),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(new ImageSize(120, 80), result.Value!.InputSize);
        Assert.Equal(new ImageSize(60, 30), result.Value.OutputSize);
        using var image = new MagickImage(output.Value);
        Assert.Equal(60u, image.Width);
        Assert.Equal(30u, image.Height);
    }

    [Fact]
    public async Task Compress_overwrites_existing_output_file()
    {
        var output = PathOf("overwrite-compress.jpg");
        await File.WriteAllTextAsync(output.Value, "old output");

        var result = await _processor.CompressAsync(
            new ImageCompressRequest(PathOf("jpeg-basic.jpg"), output, CompressionProfile.BalancedDefault()),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        using var image = new MagickImage(output.Value);
        Assert.Equal(MagickFormat.Jpeg, image.Format);
    }

    [Fact]
    public async Task Convert_overwrites_existing_output_file()
    {
        var output = PathOf("overwrite-convert.webp");
        await File.WriteAllTextAsync(output.Value, "old output");

        var result = await _processor.ConvertAsync(
            new ImageConvertRequest(PathOf("png-alpha.png"), output, ConversionProfile.WebPDefault()),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        using var image = new MagickImage(output.Value);
        Assert.Equal(MagickFormat.WebP, image.Format);
    }

    [Fact]
    public async Task Convert_write_failure_cleans_temporary_output_file()
    {
        var outputDirectory = Path.Combine(_root, "directory-as-output.webp");
        Directory.CreateDirectory(outputDirectory);

        var result = await _processor.ConvertAsync(
            new ImageConvertRequest(PathOf("png-alpha.png"), new LocalPath(outputDirectory), ConversionProfile.WebPDefault()),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AtomPixErrorCode.ImageWriteFailed, result.Error!.Code);
        Assert.Empty(TemporaryFilesIn(_root));
    }

    [Fact]
    public async Task Crop_executes_the_resolved_logical_rectangle()
    {
        var output = PathOf("cropped.png");

        var result = await _processor.CropAsync(
            new ImageCropRequest(PathOf("png-alpha.png"), output, new CropRectangle(10, 15, 60, 30), SameFormatEncodingPolicy.Default),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(new ImageSize(120, 80), result.Value!.InputSize);
        Assert.Equal(new ImageSize(60, 30), result.Value.OutputSize);
        using var image = new MagickImage(output.Value);
        Assert.Equal(60u, image.Width);
        Assert.Equal(30u, image.Height);
    }

    [Fact]
    public async Task Processing_operations_defensively_reject_input_output_path_conflicts()
    {
        var input = PathOf("jpeg-basic.jpg");
        var compress = await _processor.CompressAsync(
            new ImageCompressRequest(input, input, CompressionProfile.BalancedDefault()),
            CancellationToken.None);
        var convert = await _processor.ConvertAsync(
            new ImageConvertRequest(input, input, new ConversionProfile(OutputImageFormat.Jpeg, new ImageQuality(80), MetadataPolicy.Remove, TransparencyPolicy.Default)),
            CancellationToken.None);
        var resize = await _processor.ResizeAsync(
            new ImageResizeRequest(input, input, new ResolvedResizeSize(60, 40), SameFormatEncodingPolicy.Default),
            CancellationToken.None);
        var crop = await _processor.CropAsync(
            new ImageCropRequest(input, input, new CropRectangle(0, 0, 60, 40), SameFormatEncodingPolicy.Default),
            CancellationToken.None);

        Assert.False(compress.Succeeded);
        Assert.False(convert.Succeeded);
        Assert.False(resize.Succeeded);
        Assert.False(crop.Succeeded);
        Assert.All(
            new[] { compress.Error, convert.Error, resize.Error, crop.Error },
            error => Assert.Equal(AtomPixErrorCode.OutputPathConflictsWithInput, error!.Code));
        Assert.True(File.Exists(input.Value));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private LocalPath PathOf(string fileName) => new(Path.Combine(_root, fileName));

    private MagickImageProcessor CreateLimitedProcessor(ImageResourceCapabilities resources) =>
        new(new MagickImageProcessorOptions(
            resources,
            16UL * 1024 * 1024,
            32UL * 1024 * 1024,
            64UL * 1024 * 1024,
            1,
            Path.Combine(_root, "limited-cache")));

    private static ImageFormatKind ToImageFormatKind(OutputImageFormat format) => format switch
    {
        OutputImageFormat.Jpeg => ImageFormatKind.Jpeg,
        OutputImageFormat.Png => ImageFormatKind.Png,
        OutputImageFormat.WebP => ImageFormatKind.WebP,
        _ => ImageFormatKind.Unknown
    };
    private static IReadOnlyList<string> TemporaryFilesIn(string directory) =>
        Directory.EnumerateFiles(directory, ".*.tmp*", SearchOption.AllDirectories).ToArray();

    private sealed class ThrowingImageFileCommitter(ImageOutputFailureKind failureKind) : IImageFileCommitter
    {
        public void Commit(LocalPath outputPath, Action<string> writeTemporaryFile) =>
            throw new ImageOutputCommitException(failureKind, new IOException("Synthetic output commit failure."));
    }

    private void CreateSampleImages()
    {
        using (var image = new MagickImage(MagickColors.Red, 120, 80))
        {
            image.Format = MagickFormat.Jpeg;
            image.Write(Path.Combine(_root, "jpeg-basic.jpg"));
        }


        using (var image = new MagickImage(MagickColors.White, 320, 240))
        {
            image.Format = MagickFormat.Jpeg;
            image.Quality = 96;
            for (var y = 0; y < 240; y++)
            {
                for (var x = 0; x < 320; x++)
                {
                    var red = (byte)(x % 256);
                    var green = (byte)(y % 256);
                    var blue = (byte)((x * y) % 256);
                    image.GetPixels().SetPixel(x, y, [red, green, blue]);
                }
            }

            image.Write(Path.Combine(_root, "jpeg-detailed.jpg"));
        }
        using (var image = new MagickImage(MagickColors.Transparent, 120, 80))
        {
            image.Format = MagickFormat.Png;
            image.GetPixels().SetPixel(10, 10, [255, 0, 0, 255]);
            image.GetPixels().SetPixel(20, 20, [0, 0, 255, 128]);
            image.Write(Path.Combine(_root, "png-alpha.png"));
        }

        using (var image = new MagickImage(MagickColors.Purple, 120, 80))
        {
            image.Format = MagickFormat.Jpeg;
            var profile = new ExifProfile();
            profile.SetValue(ExifTag.ImageDescription, "AtomPix metadata sample");
            image.SetProfile(profile);
            image.Write(Path.Combine(_root, "jpeg-metadata.jpg"));
        }

        using (var image = new MagickImage(MagickColors.Blue, 120, 80))
        {
            image.Format = MagickFormat.WebP;
            image.Write(Path.Combine(_root, "webp-basic.webp"));
        }

        using (var image = new MagickImage(MagickColors.Green, 120, 80))
        {
            image.Format = MagickFormat.Bmp;
            image.Write(Path.Combine(_root, "bmp-basic.bmp"));
        }

        using (var image = new MagickImage(MagickColors.Yellow, 120, 80))
        {
            image.Format = MagickFormat.Tiff;
            image.Write(Path.Combine(_root, "tiff-basic.tiff"));
        }

        File.WriteAllText(Path.Combine(_root, "not-image.txt"), "this is not an image");
        File.WriteAllBytes(Path.Combine(_root, "corrupt.jpg"), [0xFF, 0xD8, 0x00, 0x01, 0x02]);

        using var collection = new MagickImageCollection();
        collection.Add(new MagickImage(MagickColors.Red, 80, 80) { AnimationDelay = 10 });
        collection.Add(new MagickImage(MagickColors.Blue, 80, 80) { AnimationDelay = 10 });
        collection.Write(Path.Combine(_root, "gif-animated.gif"));
    }
}







