namespace AtomPix.Imaging.Magick.Processing;

using ImageMagick;
using AtomPix.Core.Compression;
using AtomPix.Core.Conversion;
using AtomPix.Core.Errors;
using AtomPix.Core.Results;
using AtomPix.Core.ValueObjects;
using AtomPix.Imaging.Abstractions.Formats;
using AtomPix.Imaging.Abstractions.Processing;

public sealed class MagickImageProcessor : IImageProcessor
{
    public ImageProcessorCapabilities Capabilities { get; } = new(
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
        supportsAnimatedImages: false);

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

            using var collection = new MagickImageCollection(request.InputPath.Value);
            var first = collection[0];
            var format = MapFormat(first.Format);
            if (!Capabilities.SupportedInputFormats.Contains(format))
            {
                return Task.FromResult(Failure<ImageProbeResult>(AtomPixErrorCode.UnsupportedInputFormat, AtomPixErrorCategory.UnsupportedFormat, "Unsupported input format."));
            }

            var frameCount = collection.Count;
            var hasMetadata = first.GetExifProfile() is not null || first.AttributeNames.Any();
            var result = new ImageProbeResult(
                request.InputPath,
                format,
                (int)first.Width,
                (int)first.Height,
                new FileInfo(request.InputPath.Value).Length,
                first.HasAlpha,
                frameCount > 1,
                frameCount,
                hasMetadata);
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

            using var collection = new MagickImageCollection(request.InputPath.Value);
            using var image = (MagickImage)collection[0].Clone();
            image.AutoOrient();
            ResizeToMaxPixelSize(image, request.MaxPixelSize);

            var outputFormat = image.HasAlpha ? MagickFormat.Png : MagickFormat.Jpeg;
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
            if (!File.Exists(request.InputPath.Value))
            {
                return Task.FromResult(Failure<ImageCompressResult>(AtomPixErrorCode.InputFileNotFound, AtomPixErrorCategory.FileSystem, "Input file does not exist."));
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

            var outputFormat = ResolveCompressionOutputFormat(image.Format, request.OutputPath);
            if (outputFormat is null)
            {
                return Task.FromResult(Failure<ImageCompressResult>(AtomPixErrorCode.UnsupportedOutputFormat, AtomPixErrorCategory.UnsupportedFormat, "Unsupported output format."));
            }

            image.AutoOrient();
            var inputWidth = (int)image.Width;
            var inputHeight = (int)image.Height;
            ApplyResize(image, request.Profile.ResizePolicy);
            ApplyMetadata(image, request.Profile.MetadataPolicy);
            ApplyCompressionQuality(image, request.Profile, outputFormat.Value);
            image.Format = outputFormat.Value;
            WriteImageAtomically(image, request.OutputPath);

            var result = new ImageCompressResult(
                request.InputPath,
                request.OutputPath,
                MapFormat(outputFormat.Value),
                new FileInfo(request.InputPath.Value).Length,
                new FileInfo(request.OutputPath.Value).Length,
                new ImageProcessingDetails(
                    inputWidth,
                    inputHeight,
                    (int)image.Width,
                    (int)image.Height,
                    inputWidth != (int)image.Width || inputHeight != (int)image.Height,
                    request.Profile.MetadataPolicy == MetadataPolicy.Remove,
                    IsLossyFormat(outputFormat.Value)));
            return Task.FromResult(OperationResult<ImageCompressResult>.Success(result));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(Failure<ImageCompressResult>(AtomPixErrorCode.OperationCanceled, AtomPixErrorCategory.Cancellation, "Operation canceled."));
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
            if (!File.Exists(request.InputPath.Value))
            {
                return Task.FromResult(Failure<ImageConvertResult>(AtomPixErrorCode.InputFileNotFound, AtomPixErrorCategory.FileSystem, "Input file does not exist."));
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
            ApplyResize(image, request.Profile.ResizePolicy);
            ApplyMetadata(image, request.Profile.MetadataPolicy);
            if (request.Profile.Quality is { } quality && SupportsQuality(outputFormat.Value))
            {
                image.Quality = (uint)quality.Value;
            }

            image.Format = outputFormat.Value;
            WriteImageAtomically(image, request.OutputPath);

            var result = new ImageConvertResult(
                request.InputPath,
                request.OutputPath,
                inputFormat,
                MapFormat(outputFormat.Value),
                new FileInfo(request.InputPath.Value).Length,
                new FileInfo(request.OutputPath.Value).Length,
                new ImageProcessingDetails(
                    inputWidth,
                    inputHeight,
                    (int)image.Width,
                    (int)image.Height,
                    inputWidth != (int)image.Width || inputHeight != (int)image.Height,
                    request.Profile.MetadataPolicy == MetadataPolicy.Remove,
                    IsLossyFormat(outputFormat.Value)));
            return Task.FromResult(OperationResult<ImageConvertResult>.Success(result));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(Failure<ImageConvertResult>(AtomPixErrorCode.OperationCanceled, AtomPixErrorCategory.Cancellation, "Operation canceled."));
        }
        catch (Exception ex) when (ex is MagickException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Task.FromResult(Failure<ImageConvertResult>(AtomPixErrorCode.ImageConvertFailed, AtomPixErrorCategory.ImageProcessing, "Failed to convert image.", ex));
        }
    }

    private static void WriteImageAtomically(MagickImage image, LocalPath outputPath)
    {
        PrepareOutputDirectory(outputPath);
        var temporaryPath = CreateTemporaryOutputPath(outputPath);
        try
        {
            image.Write(temporaryPath);
            if (File.Exists(outputPath.Value))
            {
                File.Replace(temporaryPath, outputPath.Value, null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, outputPath.Value);
            }
        }
        finally
        {
            TryDeleteTemporaryOutput(temporaryPath);
        }
    }

    private static string CreateTemporaryOutputPath(LocalPath outputPath)
    {
        var directory = Path.GetDirectoryName(outputPath.Value);
        var fileName = Path.GetFileNameWithoutExtension(outputPath.Value);
        var extension = Path.GetExtension(outputPath.Value);
        var safeFileName = string.IsNullOrWhiteSpace(fileName) ? "atompix-output" : fileName;
        var temporaryFileName = $".{safeFileName}.{Guid.NewGuid():N}.tmp{extension}";
        return string.IsNullOrWhiteSpace(directory)
            ? temporaryFileName
            : Path.Combine(directory, temporaryFileName);
    }

    private static void TryDeleteTemporaryOutput(string temporaryPath)
    {
        try
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void PrepareOutputDirectory(LocalPath outputPath)
    {
        var directory = Path.GetDirectoryName(outputPath.Value);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
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

    private static void ApplyResize(MagickImage image, ResizePolicy policy)
    {
        switch (policy.Mode)
        {
            case ResizeMode.None:
                return;
            case ResizeMode.FitWithinBounds:
                image.Resize(new MagickGeometry((uint)(policy.MaxWidth ?? 0), (uint)(policy.MaxHeight ?? 0)) { IgnoreAspectRatio = false });
                return;
            case ResizeMode.Percentage when policy.Percentage is { } percentage:
                image.Resize(new Percentage(percentage));
                return;
        }
    }

    private static void ApplyMetadata(MagickImage image, MetadataPolicy policy)
    {
        if (policy == MetadataPolicy.Remove)
        {
            image.Strip();
        }
    }

    private static void ApplyCompressionQuality(MagickImage image, CompressionProfile profile, MagickFormat outputFormat)
    {
        if (!SupportsQuality(outputFormat))
        {
            return;
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
    }

    private static bool SupportsQuality(MagickFormat format) => format is MagickFormat.Jpeg or MagickFormat.WebP;

    private static bool IsLossyFormat(MagickFormat format) => format is MagickFormat.Jpeg or MagickFormat.WebP;

    private static MagickFormat? ResolveCompressionOutputFormat(MagickFormat inputFormat, LocalPath outputPath)
    {
        var extension = Path.GetExtension(outputPath.Value);
        if (!string.IsNullOrWhiteSpace(extension))
        {
            return MapExtension(extension);
        }

        return MapFormat(inputFormat) switch
        {
            ImageFormatKind.Jpeg => MagickFormat.Jpeg,
            ImageFormatKind.Png => MagickFormat.Png,
            ImageFormatKind.WebP => MagickFormat.WebP,
            _ => null
        };
    }

    private static MagickFormat? MapExtension(string extension) => extension.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => MagickFormat.Jpeg,
        ".png" => MagickFormat.Png,
        ".webp" => MagickFormat.WebP,
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

    private static OperationResult<T> Failure<T>(AtomPixErrorCode code, AtomPixErrorCategory category, string message, Exception? exception = null)
    {
        var details = exception is null
            ? null
            : new Dictionary<string, string> { ["exception"] = exception.GetType().Name, ["message"] = exception.Message };
        return OperationResult<T>.Failure(new AtomPixError(code, category, message, details));
    }
}

