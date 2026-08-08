namespace AtomPix.Imaging.Magick.Processing;

using ImageMagick;
using AtomPix.Core.Compression;
using AtomPix.Core.Conversion;
using AtomPix.Core.Crop;
using AtomPix.Core.Errors;
using AtomPix.Core.Results;
using AtomPix.Core.Resize;
using AtomPix.Core.ValueObjects;
using AtomPix.Imaging.Abstractions.Formats;
using AtomPix.Imaging.Abstractions.Processing;
using Microsoft.Extensions.Logging;

public sealed class MagickImageProcessor : IImageProcessor
{
    private readonly ILogger<MagickImageProcessor>? _logger;
    private readonly IImageFileCommitter _fileCommitter;

    public MagickImageProcessor()
        : this(MagickImageProcessorOptions.CreateDefault(Path.Combine(Path.GetTempPath(), "AtomPix", "Magick")), null)
    {
    }

    public MagickImageProcessor(
        MagickImageProcessorOptions options,
        ILogger<MagickImageProcessor>? logger = null,
        IImageFileCommitter? fileCommitter = null)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
        _fileCommitter = fileCommitter ?? new AtomicImageFileCommitter();
        var sameFormat = new HashSet<ImageFormatKind> { ImageFormatKind.Jpeg, ImageFormatKind.Png, ImageFormatKind.WebP, ImageFormatKind.Bmp };
        Capabilities = new ImageProcessorCapabilities(
            new HashSet<ImageFormatKind>
            {
                ImageFormatKind.Jpeg,
                ImageFormatKind.Png,
                ImageFormatKind.WebP,
                ImageFormatKind.Bmp,
                ImageFormatKind.Gif,
                ImageFormatKind.Tiff
            },
            new HashSet<OutputImageFormat>
            {
                OutputImageFormat.Jpeg,
                OutputImageFormat.Png,
                OutputImageFormat.WebP
            },
            supportsMetadata: true,
            supportsAnimatedImages: false,
            options.Resources,
            new ImageResizeCapabilities(
                sameFormat,
                options.Resources.MaxOutputWidth,
                options.Resources.MaxOutputHeight,
                options.Resources.MaxOutputPixelCount),
            new ImageCropCapabilities(
                sameFormat,
                options.Resources.MaxInputWidth,
                options.Resources.MaxInputHeight,
                options.Resources.MaxInputPixelCount));
    }

    public MagickImageProcessorOptions Options { get; }

    public ImageProcessorCapabilities Capabilities { get; }

    public Task<OperationResult<ImageProbeResult>> ProbeAsync(ImageProbeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(request.InputPath.Value))
            {
                return Task.FromResult(Failure<ImageProbeResult>(AtomPixErrorCode.InputFileNotFound, AtomPixErrorCategory.FileSystem, "Input file does not exist."));
            }

            var header = ProbeResourceHeader(request.InputPath);
            if (!header.Succeeded)
            {
                return Task.FromResult(OperationResult<ImageProbeResult>.Failure(header.Error!));
            }

            var format = MapFormat(header.Value!.Format);
            if (!Capabilities.SupportedInputFormats.Contains(format))
            {
                return Task.FromResult(Failure<ImageProbeResult>(AtomPixErrorCode.UnsupportedInputFormat, AtomPixErrorCategory.UnsupportedFormat, "Unsupported input format."));
            }

            var result = new ImageProbeResult(
                request.InputPath,
                format,
                header.Value.Width,
                header.Value.Height,
                header.Value.FileSizeBytes,
                header.Value.HasAlphaChannel,
                header.Value.HasTransparency,
                header.Value.FrameCount > 1,
                header.Value.FrameCount,
                header.Value.HasMetadata,
                header.Value.HasColorProfile);
            return Task.FromResult(OperationResult<ImageProbeResult>.Success(result));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(Failure<ImageProbeResult>(AtomPixErrorCode.OperationCanceled, AtomPixErrorCategory.Cancellation, "Operation canceled."));
        }
        catch (MagickException ex)
        {
            return Task.FromResult(Failure<ImageProbeResult>(AtomPixErrorCode.InvalidImageFile, AtomPixErrorCategory.ImageProcessing, "Input file is not a valid image or is corrupted.", ex));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Task.FromResult(Failure<ImageProbeResult>(AtomPixErrorCode.ImageReadFailed, AtomPixErrorCategory.ImageProcessing, "Failed to probe image.", ex));
        }
    }

    public Task<OperationResult<ImagePreviewResult>> CreatePreviewAsync(ImagePreviewRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(request.InputPath.Value))
            {
                return Task.FromResult(Failure<ImagePreviewResult>(AtomPixErrorCode.InputFileNotFound, AtomPixErrorCategory.FileSystem, "Input file does not exist."));
            }

            var header = ProbeResourceHeader(request.InputPath);
            if (!header.Succeeded)
            {
                return Task.FromResult(OperationResult<ImagePreviewResult>.Failure(header.Error!));
            }

            using var collection = new MagickImageCollection(request.InputPath.Value);
            using var image = (MagickImage)collection[0].Clone();
            image.AutoOrient();
            ResizeToMaxPixelSize(image, request.MaxPixelSize);

            var outputFormat = !image.IsOpaque ? MagickFormat.Png : MagickFormat.Jpeg;
            var mimeType = outputFormat == MagickFormat.Png ? "image/png" : "image/jpeg";
            using var stream = new MemoryStream();
            image.Write(stream, outputFormat);
            var result = new ImagePreviewResult(stream.ToArray(), mimeType, (int)image.Width, (int)image.Height);
            return Task.FromResult(OperationResult<ImagePreviewResult>.Success(result));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(Failure<ImagePreviewResult>(AtomPixErrorCode.OperationCanceled, AtomPixErrorCategory.Cancellation, "Operation canceled."));
        }
        catch (MagickException ex)
        {
            return Task.FromResult(Failure<ImagePreviewResult>(AtomPixErrorCode.InvalidImageFile, AtomPixErrorCategory.ImageProcessing, "Input file is not a valid image or is corrupted.", ex));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Task.FromResult(Failure<ImagePreviewResult>(AtomPixErrorCode.ImagePreviewFailed, AtomPixErrorCategory.ImageProcessing, "Failed to create preview.", ex));
        }
    }

    public Task<OperationResult<ImageCompressResult>> CompressAsync(ImageCompressRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (PathsEqual(request.InputPath, request.OutputPath))
            {
                return Task.FromResult(Failure<ImageCompressResult>(AtomPixErrorCode.OutputPathConflictsWithInput, AtomPixErrorCategory.Validation, "Output path cannot overwrite the input image."));
            }
            var outputDirectoryError = ValidateOutputDirectory(request.OutputPath);
            if (outputDirectoryError is not null)
            {
                return Task.FromResult(OperationResult<ImageCompressResult>.Failure(outputDirectoryError));
            }

            if (!File.Exists(request.InputPath.Value))
            {
                return Task.FromResult(Failure<ImageCompressResult>(AtomPixErrorCode.InputFileNotFound, AtomPixErrorCategory.FileSystem, "Input file does not exist."));
            }

            var header = ProbeResourceHeader(request.InputPath);
            if (!header.Succeeded)
            {
                return Task.FromResult(OperationResult<ImageCompressResult>.Failure(header.Error!));
            }

            using var collection = new MagickImageCollection(request.InputPath.Value);
            if (collection.Count > 1)
            {
                return Task.FromResult(Failure<ImageCompressResult>(AtomPixErrorCode.UnsupportedInputFormat, AtomPixErrorCategory.UnsupportedFormat, "Multi-frame images are not supported for compression in the first release."));
            }

            using var image = (MagickImage)collection[0].Clone();
            var inputFormat = MapFormat(image.Format);
            if (!Capabilities.SupportedInputFormats.Contains(inputFormat))
            {
                return Task.FromResult(Failure<ImageCompressResult>(AtomPixErrorCode.UnsupportedInputFormat, AtomPixErrorCategory.UnsupportedFormat, "Unsupported input format."));
            }

            var outputFormat = ResolveSameFormatOutputFormat(image.Format, request.OutputPath, allowBmp: false);
            if (outputFormat is null)
            {
                return Task.FromResult(Failure<ImageCompressResult>(AtomPixErrorCode.UnsupportedOutputFormat, AtomPixErrorCategory.UnsupportedFormat, "Unsupported output format."));
            }

            image.AutoOrient();
            var inputWidth = (int)image.Width;
            var inputHeight = (int)image.Height;
            ApplyMetadata(image, request.Profile.MetadataPolicy);
            var appliedQuality = ApplyCompressionQuality(image, request.Profile, outputFormat.Value);
            image.Format = outputFormat.Value;
            _fileCommitter.Commit(request.OutputPath, image.Write);

            var result = new ImageCompressResult(
                request.InputPath,
                request.OutputPath,
                inputFormat,
                MapFormat(outputFormat.Value),
                new FileInfo(request.InputPath.Value).Length,
                new FileInfo(request.OutputPath.Value).Length,
                appliedQuality,
                new ImageProcessingDetails(
                    inputWidth,
                    inputHeight,
                    (int)image.Width,
                    (int)image.Height,
                    request.Profile.MetadataPolicy == MetadataPolicy.Remove,
                    IsLossyFormat(outputFormat.Value)));
            return Task.FromResult(OperationResult<ImageCompressResult>.Success(result));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(Failure<ImageCompressResult>(AtomPixErrorCode.OperationCanceled, AtomPixErrorCategory.Cancellation, "Operation canceled."));
        }
        catch (ImageOutputCommitException ex)
        {
            return Task.FromResult(OutputFailure<ImageCompressResult>(ex));
        }
        catch (Exception ex) when (ex is MagickException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Task.FromResult(Failure<ImageCompressResult>(AtomPixErrorCode.ImageCompressFailed, AtomPixErrorCategory.ImageProcessing, "Failed to compress image.", ex));
        }
    }

    public Task<OperationResult<ImageConvertResult>> ConvertAsync(ImageConvertRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (PathsEqual(request.InputPath, request.OutputPath))
            {
                return Task.FromResult(Failure<ImageConvertResult>(AtomPixErrorCode.OutputPathConflictsWithInput, AtomPixErrorCategory.Validation, "Output path cannot overwrite the input image."));
            }
            var outputDirectoryError = ValidateOutputDirectory(request.OutputPath);
            if (outputDirectoryError is not null)
            {
                return Task.FromResult(OperationResult<ImageConvertResult>.Failure(outputDirectoryError));
            }

            if (!File.Exists(request.InputPath.Value))
            {
                return Task.FromResult(Failure<ImageConvertResult>(AtomPixErrorCode.InputFileNotFound, AtomPixErrorCategory.FileSystem, "Input file does not exist."));
            }

            var header = ProbeResourceHeader(request.InputPath);
            if (!header.Succeeded)
            {
                return Task.FromResult(OperationResult<ImageConvertResult>.Failure(header.Error!));
            }

            using var collection = new MagickImageCollection(request.InputPath.Value);
            if (collection.Count > 1)
            {
                return Task.FromResult(Failure<ImageConvertResult>(AtomPixErrorCode.UnsupportedInputFormat, AtomPixErrorCategory.UnsupportedFormat, "Multi-frame images are not supported for conversion in the first release."));
            }

            using var image = (MagickImage)collection[0].Clone();
            var inputFormat = MapFormat(image.Format);
            if (!Capabilities.SupportedInputFormats.Contains(inputFormat))
            {
                return Task.FromResult(Failure<ImageConvertResult>(AtomPixErrorCode.UnsupportedInputFormat, AtomPixErrorCategory.UnsupportedFormat, "Unsupported input format."));
            }

            var outputFormat = MapOutputFormat(request.Profile.OutputFormat);
            if (outputFormat is null)
            {
                return Task.FromResult(Failure<ImageConvertResult>(AtomPixErrorCode.UnsupportedOutputFormat, AtomPixErrorCategory.UnsupportedFormat, "Unsupported output format."));
            }

            image.AutoOrient();
            var inputWidth = (int)image.Width;
            var inputHeight = (int)image.Height;
            var transparency = ApplyTransparency(image, outputFormat.Value, request.Profile.TransparencyPolicy);
            ApplyMetadata(image, request.Profile.MetadataPolicy);
            if (request.Profile.Quality is { } quality && SupportsQuality(outputFormat.Value))
            {
                image.Quality = (uint)quality.Value;
            }

            image.Format = outputFormat.Value;
            _fileCommitter.Commit(request.OutputPath, image.Write);

            var result = new ImageConvertResult(
                request.InputPath,
                request.OutputPath,
                inputFormat,
                MapFormat(outputFormat.Value),
                new FileInfo(request.InputPath.Value).Length,
                new FileInfo(request.OutputPath.Value).Length,
                transparency,
                new ImageProcessingDetails(
                    inputWidth,
                    inputHeight,
                    (int)image.Width,
                    (int)image.Height,
                    request.Profile.MetadataPolicy == MetadataPolicy.Remove,
                    IsLossyFormat(outputFormat.Value)));
            return Task.FromResult(OperationResult<ImageConvertResult>.Success(result));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(Failure<ImageConvertResult>(AtomPixErrorCode.OperationCanceled, AtomPixErrorCategory.Cancellation, "Operation canceled."));
        }
        catch (ImageOutputCommitException ex)
        {
            return Task.FromResult(OutputFailure<ImageConvertResult>(ex));
        }
        catch (Exception ex) when (ex is MagickException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Task.FromResult(Failure<ImageConvertResult>(AtomPixErrorCode.ImageConvertFailed, AtomPixErrorCategory.ImageProcessing, "Failed to convert image.", ex));
        }
    }

    public Task<OperationResult<ImageResizeResult>> ResizeAsync(ImageResizeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (PathsEqual(request.InputPath, request.OutputPath))
            {
                return Task.FromResult(Failure<ImageResizeResult>(AtomPixErrorCode.OutputPathConflictsWithInput, AtomPixErrorCategory.Validation, "Output path cannot overwrite the input image."));
            }
            var outputDirectoryError = ValidateOutputDirectory(request.OutputPath);
            if (outputDirectoryError is not null)
            {
                return Task.FromResult(OperationResult<ImageResizeResult>.Failure(outputDirectoryError));
            }

            if (!File.Exists(request.InputPath.Value))
            {
                return Task.FromResult(Failure<ImageResizeResult>(AtomPixErrorCode.InputFileNotFound, AtomPixErrorCategory.FileSystem, "Input file does not exist."));
            }

            var header = ProbeResourceHeader(request.InputPath);
            if (!header.Succeeded)
            {
                return Task.FromResult(OperationResult<ImageResizeResult>.Failure(header.Error!));
            }
            var targetError = ValidateOutputDimensions(request.TargetSize.Width, request.TargetSize.Height);
            if (targetError is not null)
            {
                return Task.FromResult(OperationResult<ImageResizeResult>.Failure(targetError));
            }

            using var collection = new MagickImageCollection(request.InputPath.Value);
            if (collection.Count > 1)
            {
                return Task.FromResult(Failure<ImageResizeResult>(AtomPixErrorCode.UnsupportedInputFormat, AtomPixErrorCategory.UnsupportedFormat, "Multi-frame images are not supported for resize in the first release."));
            }

            using var image = (MagickImage)collection[0].Clone();
            var inputFormat = MapFormat(image.Format);
            if (Capabilities.Resize is null || !Capabilities.Resize.SupportedSameFormatFormats.Contains(inputFormat))
            {
                return Task.FromResult(Failure<ImageResizeResult>(AtomPixErrorCode.UnsupportedInputFormat, AtomPixErrorCategory.UnsupportedFormat, "Input format does not support same-format resize."));
            }

            var outputFormat = ResolveSameFormatOutputFormat(image.Format, request.OutputPath, allowBmp: true);
            if (outputFormat is null)
            {
                return Task.FromResult(Failure<ImageResizeResult>(AtomPixErrorCode.UnsupportedOutputFormat, AtomPixErrorCategory.UnsupportedFormat, "Resize output must preserve the input format."));
            }

            image.AutoOrient();
            var inputSize = new ImageSize((int)image.Width, (int)image.Height);
            image.Resize(new MagickGeometry((uint)request.TargetSize.Width, (uint)request.TargetSize.Height) { IgnoreAspectRatio = true });
            if ((int)image.Width != request.TargetSize.Width || (int)image.Height != request.TargetSize.Height)
            {
                return Task.FromResult(Failure<ImageResizeResult>(AtomPixErrorCode.ImageResizeFailed, AtomPixErrorCategory.ImageProcessing, "Image engine did not produce the requested resize dimensions."));
            }

            ApplySameFormatEncoding(image, outputFormat.Value, request.EncodingPolicy);
            image.Format = outputFormat.Value;
            _fileCommitter.Commit(request.OutputPath, image.Write);

            return Task.FromResult(OperationResult<ImageResizeResult>.Success(new ImageResizeResult(
                request.InputPath,
                request.OutputPath,
                inputFormat,
                inputSize,
                new ImageSize((int)image.Width, (int)image.Height),
                new FileInfo(request.InputPath.Value).Length,
                new FileInfo(request.OutputPath.Value).Length)));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(Failure<ImageResizeResult>(AtomPixErrorCode.OperationCanceled, AtomPixErrorCategory.Cancellation, "Operation canceled."));
        }
        catch (ImageOutputCommitException ex)
        {
            return Task.FromResult(OutputFailure<ImageResizeResult>(ex));
        }
        catch (Exception ex) when (ex is MagickException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Task.FromResult(Failure<ImageResizeResult>(AtomPixErrorCode.ImageResizeFailed, AtomPixErrorCategory.ImageProcessing, "Failed to resize image.", ex));
        }
    }

    public Task<OperationResult<ImageCropResult>> CropAsync(ImageCropRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (PathsEqual(request.InputPath, request.OutputPath))
            {
                return Task.FromResult(Failure<ImageCropResult>(AtomPixErrorCode.OutputPathConflictsWithInput, AtomPixErrorCategory.Validation, "Output path cannot overwrite the input image."));
            }
            var outputDirectoryError = ValidateOutputDirectory(request.OutputPath);
            if (outputDirectoryError is not null)
            {
                return Task.FromResult(OperationResult<ImageCropResult>.Failure(outputDirectoryError));
            }

            if (!File.Exists(request.InputPath.Value))
            {
                return Task.FromResult(Failure<ImageCropResult>(AtomPixErrorCode.InputFileNotFound, AtomPixErrorCategory.FileSystem, "Input file does not exist."));
            }

            var header = ProbeResourceHeader(request.InputPath);
            if (!header.Succeeded)
            {
                return Task.FromResult(OperationResult<ImageCropResult>.Failure(header.Error!));
            }

            using var collection = new MagickImageCollection(request.InputPath.Value);
            if (collection.Count > 1)
            {
                return Task.FromResult(Failure<ImageCropResult>(AtomPixErrorCode.UnsupportedInputFormat, AtomPixErrorCategory.UnsupportedFormat, "Multi-frame images are not supported for crop in the first release."));
            }

            using var image = (MagickImage)collection[0].Clone();
            var inputFormat = MapFormat(image.Format);
            if (Capabilities.Crop is null || !Capabilities.Crop.SupportedSameFormatFormats.Contains(inputFormat))
            {
                return Task.FromResult(Failure<ImageCropResult>(AtomPixErrorCode.UnsupportedInputFormat, AtomPixErrorCategory.UnsupportedFormat, "Input format does not support same-format crop."));
            }

            var outputFormat = ResolveSameFormatOutputFormat(image.Format, request.OutputPath, allowBmp: true);
            if (outputFormat is null)
            {
                return Task.FromResult(Failure<ImageCropResult>(AtomPixErrorCode.UnsupportedOutputFormat, AtomPixErrorCategory.UnsupportedFormat, "Crop output must preserve the input format."));
            }

            image.AutoOrient();
            var inputSize = new ImageSize((int)image.Width, (int)image.Height);
            var validation = CropRules.ValidateCropRectangle(inputSize, request.CropArea);
            if (!validation.Succeeded)
            {
                return Task.FromResult(OperationResult<ImageCropResult>.Failure(validation.Error!));
            }

            image.Crop(new MagickGeometry(
                request.CropArea.X,
                request.CropArea.Y,
                (uint)request.CropArea.Width,
                (uint)request.CropArea.Height));
            image.ResetPage();
            if ((int)image.Width != request.CropArea.Width || (int)image.Height != request.CropArea.Height)
            {
                return Task.FromResult(Failure<ImageCropResult>(AtomPixErrorCode.ImageCropFailed, AtomPixErrorCategory.ImageProcessing, "Image engine did not produce the requested crop dimensions."));
            }

            ApplySameFormatEncoding(image, outputFormat.Value, request.EncodingPolicy);
            image.Format = outputFormat.Value;
            _fileCommitter.Commit(request.OutputPath, image.Write);

            return Task.FromResult(OperationResult<ImageCropResult>.Success(new ImageCropResult(
                request.InputPath,
                request.OutputPath,
                inputFormat,
                inputSize,
                new ImageSize((int)image.Width, (int)image.Height),
                new FileInfo(request.InputPath.Value).Length,
                new FileInfo(request.OutputPath.Value).Length)));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(Failure<ImageCropResult>(AtomPixErrorCode.OperationCanceled, AtomPixErrorCategory.Cancellation, "Operation canceled."));
        }
        catch (ImageOutputCommitException ex)
        {
            return Task.FromResult(OutputFailure<ImageCropResult>(ex));
        }
        catch (Exception ex) when (ex is MagickException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Task.FromResult(Failure<ImageCropResult>(AtomPixErrorCode.ImageCropFailed, AtomPixErrorCategory.ImageProcessing, "Failed to crop image.", ex));
        }
    }

    private static AtomPixError? ValidateOutputDirectory(LocalPath outputPath)
    {
        var directory = Path.GetDirectoryName(outputPath.Value);
        return string.IsNullOrWhiteSpace(directory) || Directory.Exists(directory)
            ? null
            : new AtomPixError(
                AtomPixErrorCode.OutputDirectoryNotFound,
                AtomPixErrorCategory.FileSystem,
                "Output directory does not exist.");
    }

    private static void ResizeToMaxPixelSize(MagickImage image, int maxPixelSize)
    {
        if (maxPixelSize <= 0)
        {
            return;
        }

        if (image.Width <= maxPixelSize && image.Height <= maxPixelSize)
        {
            return;
        }

        image.Resize(new MagickGeometry((uint)maxPixelSize, (uint)maxPixelSize) { IgnoreAspectRatio = false });
    }

    private static TransparencyProcessingResult ApplyTransparency(MagickImage image, MagickFormat outputFormat, TransparencyPolicy policy)
    {
        if (image.IsOpaque)
        {
            return new TransparencyProcessingResult(TransparencyOutcome.NotPresent, null);
        }

        if (outputFormat is MagickFormat.Png or MagickFormat.WebP)
        {
            return new TransparencyProcessingResult(TransparencyOutcome.Preserved, null);
        }

        var background = policy.OpaqueBackgroundColor;
        image.BackgroundColor = new MagickColor(background.Red, background.Green, background.Blue);
        image.Alpha(AlphaOption.Remove);
        return new TransparencyProcessingResult(TransparencyOutcome.Flattened, background);
    }

    private OperationResult<ResourceHeader> ProbeResourceHeader(LocalPath inputPath)
    {
        var fileInfo = new FileInfo(inputPath.Value);
        if (!fileInfo.Exists)
        {
            return OperationResult<ResourceHeader>.Failure(new AtomPixError(
                AtomPixErrorCode.InputFileNotFound,
                AtomPixErrorCategory.FileSystem,
                "Input file does not exist."));
        }

        var resources = Capabilities.Resources;
        if (fileInfo.Length > resources.MaxInputFileSizeBytes)
        {
            return OperationResult<ResourceHeader>.Failure(ResourceValueError(
                AtomPixErrorCode.InputFileTooLarge,
                "Input image file exceeds the image processor limit.",
                "InputFileSizeBytes",
                fileInfo.Length,
                resources.MaxInputFileSizeBytes));
        }

        using var collection = new MagickImageCollection();
        collection.Ping(inputPath.Value);
        if (collection.Count == 0)
        {
            return OperationResult<ResourceHeader>.Failure(new AtomPixError(
                AtomPixErrorCode.InvalidImageFile,
                AtomPixErrorCategory.ImageProcessing,
                "Input file does not contain an image frame."));
        }

        var first = collection[0];
        var width = checked((int)first.Width);
        var height = checked((int)first.Height);
        var pixels = checked((long)width * height);
        if (width > resources.MaxInputWidth
            || height > resources.MaxInputHeight
            || pixels > resources.MaxInputPixelCount)
        {
            return OperationResult<ResourceHeader>.Failure(DimensionLimitError(
                "Input image dimensions exceed the image processor limit.",
                "InputDimensions",
                width,
                height,
                pixels,
                resources.MaxInputWidth,
                resources.MaxInputHeight,
                resources.MaxInputPixelCount));
        }

        return OperationResult<ResourceHeader>.Success(new ResourceHeader(
            first.Format,
            width,
            height,
            fileInfo.Length,
            first.HasAlpha,
            !first.IsOpaque,
            collection.Count,
            first.GetExifProfile() is not null || first.AttributeNames.Any(),
            first.GetColorProfile() is not null));
    }

    private AtomPixError? ValidateOutputDimensions(int width, int height)
    {
        var resources = Capabilities.Resources;
        var pixels = checked((long)width * height);
        if (width <= resources.MaxOutputWidth
            && height <= resources.MaxOutputHeight
            && pixels <= resources.MaxOutputPixelCount)
        {
            return null;
        }

        return DimensionLimitError(
            "Output image dimensions exceed the image processor limit.",
            "OutputDimensions",
            width,
            height,
            pixels,
            resources.MaxOutputWidth,
            resources.MaxOutputHeight,
            resources.MaxOutputPixelCount);
    }

    private static AtomPixError ResourceValueError(
        AtomPixErrorCode code,
        string message,
        string resourceKind,
        long actualValue,
        long maximumValue) =>
        new(
            code,
            AtomPixErrorCategory.Validation,
            message,
            new Dictionary<string, string>
            {
                ["ResourceKind"] = resourceKind,
                ["ActualValue"] = actualValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["MaximumValue"] = maximumValue.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });

    private static AtomPixError DimensionLimitError(
        string message,
        string resourceKind,
        int width,
        int height,
        long pixels,
        int maximumWidth,
        int maximumHeight,
        long maximumPixels) =>
        new(
            AtomPixErrorCode.ImageDimensionsExceedLimit,
            AtomPixErrorCategory.Validation,
            message,
            new Dictionary<string, string>
            {
                ["ResourceKind"] = resourceKind,
                ["ActualWidth"] = width.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["ActualHeight"] = height.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["ActualPixelCount"] = pixels.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["MaximumWidth"] = maximumWidth.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["MaximumHeight"] = maximumHeight.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["MaximumPixelCount"] = maximumPixels.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });

    private sealed record ResourceHeader(
        MagickFormat Format,
        int Width,
        int Height,
        long FileSizeBytes,
        bool HasAlphaChannel,
        bool HasTransparency,
        int FrameCount,
        bool HasMetadata,
        bool HasColorProfile);

    private static bool PathsEqual(LocalPath left, LocalPath right)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(Path.GetFullPath(left.Value), Path.GetFullPath(right.Value), comparison);
    }

    private static void ApplyMetadata(MagickImage image, MetadataPolicy policy)
    {
        if (policy == MetadataPolicy.Remove)
        {
            var colorProfile = image.GetColorProfile();
            image.Strip();
            if (colorProfile is not null)
            {
                image.SetProfile(colorProfile);
            }
        }
    }

    private static ImageQuality? ApplyCompressionQuality(MagickImage image, CompressionProfile profile, MagickFormat outputFormat)
    {
        if (!SupportsQuality(outputFormat))
        {
            return null;
        }

        var quality = profile.Mode switch
        {
            CompressionMode.HighQuality => 90,
            CompressionMode.Balanced => 80,
            CompressionMode.Maximum => 65,
            CompressionMode.Custom => profile.Quality?.Value ?? 80,
            CompressionMode.Smart => outputFormat == MagickFormat.WebP ? 80 : 82,
            _ => 80
        };
        image.Quality = (uint)quality;
        return new ImageQuality(quality);
    }

    private static void ApplySameFormatEncoding(MagickImage image, MagickFormat outputFormat, SameFormatEncodingPolicy policy)
    {
        ApplyMetadata(image, policy.MetadataPolicy);
        if (SupportsQuality(outputFormat))
        {
            image.Quality = (uint)policy.LossyQuality.Value;
        }
    }

    private static bool SupportsQuality(MagickFormat format) => format is MagickFormat.Jpeg or MagickFormat.WebP;

    private static bool IsLossyFormat(MagickFormat format) => format is MagickFormat.Jpeg or MagickFormat.WebP;

    private static MagickFormat? ResolveSameFormatOutputFormat(MagickFormat inputFormat, LocalPath outputPath, bool allowBmp)
    {
        var mappedInput = MapFormat(inputFormat);
        if (mappedInput is not (ImageFormatKind.Jpeg or ImageFormatKind.Png or ImageFormatKind.WebP)
            && (!allowBmp || mappedInput != ImageFormatKind.Bmp))
        {
            return null;
        }

        var extensionFormat = MapExtension(Path.GetExtension(outputPath.Value));
        return extensionFormat is not null && MapFormat(extensionFormat.Value) == mappedInput
            ? extensionFormat
            : null;
    }

    private static MagickFormat? MapExtension(string extension) => extension.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => MagickFormat.Jpeg,
        ".png" => MagickFormat.Png,
        ".webp" => MagickFormat.WebP,
        ".bmp" => MagickFormat.Bmp,
        _ => null
    };

    private static MagickFormat? MapOutputFormat(OutputImageFormat format) => format switch
    {
        OutputImageFormat.Jpeg => MagickFormat.Jpeg,
        OutputImageFormat.Png => MagickFormat.Png,
        OutputImageFormat.WebP => MagickFormat.WebP,
        _ => null
    };

    private static ImageFormatKind MapFormat(MagickFormat format) => format switch
    {
        MagickFormat.Jpeg => ImageFormatKind.Jpeg,
        MagickFormat.Png => ImageFormatKind.Png,
        MagickFormat.WebP => ImageFormatKind.WebP,
        MagickFormat.Bmp => ImageFormatKind.Bmp,
        MagickFormat.Gif => ImageFormatKind.Gif,
        MagickFormat.Tiff => ImageFormatKind.Tiff,
        _ => ImageFormatKind.Unknown
    };

    private OperationResult<T> OutputFailure<T>(ImageOutputCommitException exception) => exception.Kind switch
    {
        ImageOutputFailureKind.InsufficientDiskSpace => Failure<T>(
            AtomPixErrorCode.InsufficientDiskSpace,
            AtomPixErrorCategory.FileSystem,
            "Output volume has insufficient free space.",
            exception.InnerException ?? exception),
        ImageOutputFailureKind.PermissionDenied => Failure<T>(
            AtomPixErrorCode.ImageWriteFailed,
            AtomPixErrorCategory.Permission,
            "Permission was denied while writing the output image.",
            exception.InnerException ?? exception),
        _ => Failure<T>(
            AtomPixErrorCode.ImageWriteFailed,
            AtomPixErrorCategory.FileSystem,
            "Failed to commit the output image.",
            exception.InnerException ?? exception)
    };

    private OperationResult<T> Failure<T>(AtomPixErrorCode code, AtomPixErrorCategory category, string message, Exception? exception = null)
    {
        if (exception is not null)
        {
            _logger?.Log(
                LogLevel.Warning,
                new EventId(2001, "ImageEngineFailure"),
                new Dictionary<string, object?>
                {
                    ["ErrorCode"] = code.ToString(),
                    ["ErrorCategory"] = category.ToString(),
                    ["ImageEngineVersion"] = MagickNET.Version
                },
                exception,
                static (_, error) => error?.Message ?? "Image engine failure.");
        }

        return OperationResult<T>.Failure(new AtomPixError(code, category, message));
    }
}

