namespace AtomPix.Imaging.Abstractions.Processing;

using AtomPix.Core.Compression;
using AtomPix.Core.Conversion;
using AtomPix.Core.Crop;
using AtomPix.Core.Resize;
using AtomPix.Core.Results;
using AtomPix.Core.ValueObjects;
using AtomPix.Imaging.Abstractions.Formats;
using static AtomPix.Imaging.Abstractions.Processing.ImageProcessingContractValidation;

public interface IImageProcessor
{
    ImageProcessorCapabilities Capabilities { get; }

    Task<OperationResult<ImageProbeResult>> ProbeAsync(
        ImageProbeRequest request,
        CancellationToken cancellationToken);

    Task<OperationResult<ImagePreviewResult>> CreatePreviewAsync(
        ImagePreviewRequest request,
        CancellationToken cancellationToken);

    Task<OperationResult<ImageCompressResult>> CompressAsync(
        ImageCompressRequest request,
        CancellationToken cancellationToken);

    Task<OperationResult<ImageConvertResult>> ConvertAsync(
        ImageConvertRequest request,
        CancellationToken cancellationToken);

    Task<OperationResult<ImageResizeResult>> ResizeAsync(
        ImageResizeRequest request,
        CancellationToken cancellationToken);

    Task<OperationResult<ImageCropResult>> CropAsync(
        ImageCropRequest request,
        CancellationToken cancellationToken);
}

public sealed record ImageProcessorCapabilities
{
    public ImageProcessorCapabilities(
        IReadOnlySet<ImageFormatKind> supportedInputFormats,
        IReadOnlySet<OutputImageFormat> supportedOutputFormats,
        bool supportsMetadata,
        bool supportsAnimatedImages,
        ImageResourceCapabilities resources,
        ImageResizeCapabilities? resize,
        ImageCropCapabilities? crop)
    {
        if (supportedInputFormats is null)
        {
            throw new ArgumentNullException(nameof(supportedInputFormats));
        }

        if (supportedOutputFormats is null)
        {
            throw new ArgumentNullException(nameof(supportedOutputFormats));
        }

        var inputFormats = supportedInputFormats.ToHashSet();
        var outputFormats = supportedOutputFormats.ToHashSet();
        if (inputFormats.Count == 0)
        {
            throw new ArgumentException("At least one input format must be declared.", nameof(supportedInputFormats));
        }

        if (outputFormats.Count == 0)
        {
            throw new ArgumentException("At least one output format must be declared.", nameof(supportedOutputFormats));
        }

        if (inputFormats.Contains(ImageFormatKind.Unknown))
        {
            throw new ArgumentException("Unknown cannot be declared as a supported input format.", nameof(supportedInputFormats));
        }

        SupportedInputFormats = inputFormats;
        SupportedOutputFormats = outputFormats;
        SupportsMetadata = supportsMetadata;
        SupportsAnimatedImages = supportsAnimatedImages;
        Resources = resources ?? throw new ArgumentNullException(nameof(resources));
        Resize = resize;
        Crop = crop;
    }

    public IReadOnlySet<ImageFormatKind> SupportedInputFormats { get; }

    public IReadOnlySet<OutputImageFormat> SupportedOutputFormats { get; }

    public bool SupportsMetadata { get; }

    public bool SupportsAnimatedImages { get; }

    public ImageResourceCapabilities Resources { get; }

    public ImageResizeCapabilities? Resize { get; }

    public ImageCropCapabilities? Crop { get; }
}

public sealed record ImageResourceCapabilities
{
    public ImageResourceCapabilities(
        long maxInputFileSizeBytes,
        int maxInputWidth,
        int maxInputHeight,
        long maxInputPixelCount,
        int maxOutputWidth,
        int maxOutputHeight,
        long maxOutputPixelCount)
    {
        ValidatePositive(maxInputFileSizeBytes, nameof(maxInputFileSizeBytes));
        ValidatePositive(maxInputWidth, nameof(maxInputWidth));
        ValidatePositive(maxInputHeight, nameof(maxInputHeight));
        ValidatePositive(maxInputPixelCount, nameof(maxInputPixelCount));
        ValidatePositive(maxOutputWidth, nameof(maxOutputWidth));
        ValidatePositive(maxOutputHeight, nameof(maxOutputHeight));
        ValidatePositive(maxOutputPixelCount, nameof(maxOutputPixelCount));

        MaxInputFileSizeBytes = maxInputFileSizeBytes;
        MaxInputWidth = maxInputWidth;
        MaxInputHeight = maxInputHeight;
        MaxInputPixelCount = maxInputPixelCount;
        MaxOutputWidth = maxOutputWidth;
        MaxOutputHeight = maxOutputHeight;
        MaxOutputPixelCount = maxOutputPixelCount;
    }

    public long MaxInputFileSizeBytes { get; }
    public int MaxInputWidth { get; }
    public int MaxInputHeight { get; }
    public long MaxInputPixelCount { get; }
    public int MaxOutputWidth { get; }
    public int MaxOutputHeight { get; }
    public long MaxOutputPixelCount { get; }
}

public sealed record ImageResizeCapabilities
{
    public ImageResizeCapabilities(IReadOnlySet<ImageFormatKind> supportedSameFormatFormats, int maxWidth, int maxHeight, long maxPixelCount)
    {
        SupportedSameFormatFormats = ValidateSameFormatSet(supportedSameFormatFormats, nameof(supportedSameFormatFormats));
        ValidatePositive(maxWidth, nameof(maxWidth));
        ValidatePositive(maxHeight, nameof(maxHeight));
        ValidatePositive(maxPixelCount, nameof(maxPixelCount));
        MaxWidth = maxWidth;
        MaxHeight = maxHeight;
        MaxPixelCount = maxPixelCount;
    }

    public IReadOnlySet<ImageFormatKind> SupportedSameFormatFormats { get; }
    public int MaxWidth { get; }
    public int MaxHeight { get; }
    public long MaxPixelCount { get; }
}

public sealed record ImageCropCapabilities
{
    public ImageCropCapabilities(IReadOnlySet<ImageFormatKind> supportedSameFormatFormats, int maxInputWidth, int maxInputHeight, long maxInputPixelCount)
    {
        SupportedSameFormatFormats = ValidateSameFormatSet(supportedSameFormatFormats, nameof(supportedSameFormatFormats));
        ValidatePositive(maxInputWidth, nameof(maxInputWidth));
        ValidatePositive(maxInputHeight, nameof(maxInputHeight));
        ValidatePositive(maxInputPixelCount, nameof(maxInputPixelCount));
        MaxInputWidth = maxInputWidth;
        MaxInputHeight = maxInputHeight;
        MaxInputPixelCount = maxInputPixelCount;
    }

    public IReadOnlySet<ImageFormatKind> SupportedSameFormatFormats { get; }
    public int MaxInputWidth { get; }
    public int MaxInputHeight { get; }
    public long MaxInputPixelCount { get; }
}

public sealed record ImageProbeRequest(LocalPath InputPath);

public sealed record ImageProbeResult
{
    public ImageProbeResult(
        LocalPath inputPath,
        ImageFormatKind format,
        int width,
        int height,
        long fileSizeBytes,
        bool hasAlphaChannel,
        bool hasTransparency,
        bool isAnimated,
        int frameCount,
        bool hasMetadata,
        bool hasColorProfile)
    {
        ValidateKnownFormat(format, nameof(format));
        ValidatePositive(width, nameof(width));
        ValidatePositive(height, nameof(height));
        ValidateNonNegative(fileSizeBytes, nameof(fileSizeBytes));
        ValidatePositive(frameCount, nameof(frameCount));

        if (isAnimated && frameCount <= 1)
        {
            throw new ArgumentException("Animated images must report more than one frame.", nameof(frameCount));
        }

        if (hasTransparency && !hasAlphaChannel)
        {
            throw new ArgumentException("Transparent images must report an alpha channel.", nameof(hasTransparency));
        }

        InputPath = inputPath;
        Format = format;
        Width = width;
        Height = height;
        FileSizeBytes = fileSizeBytes;
        HasAlphaChannel = hasAlphaChannel;
        HasTransparency = hasTransparency;
        IsAnimated = isAnimated;
        FrameCount = frameCount;
        HasMetadata = hasMetadata;
        HasColorProfile = hasColorProfile;
    }

    public LocalPath InputPath { get; }

    public ImageFormatKind Format { get; }

    public int Width { get; }

    public int Height { get; }

    public long FileSizeBytes { get; }

    public bool HasAlphaChannel { get; }

    public bool HasTransparency { get; }

    public bool IsAnimated { get; }

    public int FrameCount { get; }

    public bool HasMetadata { get; }

    public bool HasColorProfile { get; }
}

public sealed record ImagePreviewRequest
{
    public ImagePreviewRequest(LocalPath inputPath, int maxPixelSize)
    {
        ValidatePositive(maxPixelSize, nameof(maxPixelSize));
        InputPath = inputPath;
        MaxPixelSize = maxPixelSize;
    }

    public LocalPath InputPath { get; }

    public int MaxPixelSize { get; }
}

public sealed record ImagePreviewResult
{
    public ImagePreviewResult(byte[] encodedBytes, string mimeType, int width, int height)
    {
        if (encodedBytes is null)
        {
            throw new ArgumentNullException(nameof(encodedBytes));
        }

        if (encodedBytes.Length == 0)
        {
            throw new ArgumentException("Preview bytes cannot be empty.", nameof(encodedBytes));
        }

        if (string.IsNullOrWhiteSpace(mimeType))
        {
            throw new ArgumentException("Preview MIME type cannot be empty.", nameof(mimeType));
        }

        ValidatePositive(width, nameof(width));
        ValidatePositive(height, nameof(height));

        _encodedBytes = encodedBytes.ToArray();
        MimeType = mimeType;
        Width = width;
        Height = height;
    }

    private readonly byte[] _encodedBytes;

    public byte[] EncodedBytes => _encodedBytes.ToArray();

    public string MimeType { get; }

    public int Width { get; }

    public int Height { get; }
}


public sealed record ImageProcessingDetails
{
    public ImageProcessingDetails(
        int inputWidth,
        int inputHeight,
        int outputWidth,
        int outputHeight,
        bool metadataRemoved,
        bool lossyOutput)
    {
        ValidatePositive(inputWidth, nameof(inputWidth));
        ValidatePositive(inputHeight, nameof(inputHeight));
        ValidatePositive(outputWidth, nameof(outputWidth));
        ValidatePositive(outputHeight, nameof(outputHeight));

        if (inputWidth != outputWidth || inputHeight != outputHeight)
        {
            throw new ArgumentException("Compression and conversion details cannot report a size change.");
        }

        InputWidth = inputWidth;
        InputHeight = inputHeight;
        OutputWidth = outputWidth;
        OutputHeight = outputHeight;
        MetadataRemoved = metadataRemoved;
        LossyOutput = lossyOutput;
    }

    public int InputWidth { get; }

    public int InputHeight { get; }

    public int OutputWidth { get; }

    public int OutputHeight { get; }

    public bool MetadataRemoved { get; }

    public bool LossyOutput { get; }
}
public sealed record ImageCompressRequest
{
    public ImageCompressRequest(LocalPath inputPath, LocalPath outputPath, CompressionProfile profile)
    {
        InputPath = inputPath;
        OutputPath = outputPath;
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
    }

    public LocalPath InputPath { get; }

    public LocalPath OutputPath { get; }

    public CompressionProfile Profile { get; }
}

public sealed record ImageCompressResult
{
    public ImageCompressResult(
        LocalPath inputPath,
        LocalPath outputPath,
        ImageFormatKind inputFormat,
        ImageFormatKind outputFormat,
        long inputSizeBytes,
        long outputSizeBytes,
        ImageQuality? appliedQuality,
        ImageProcessingDetails? details = null)
    {
        ValidateKnownFormat(inputFormat, nameof(inputFormat));
        ValidateKnownFormat(outputFormat, nameof(outputFormat));
        if (inputFormat != outputFormat)
        {
            throw new ArgumentException("Compression must preserve the input image format.", nameof(outputFormat));
        }

        ValidateNonNegative(inputSizeBytes, nameof(inputSizeBytes));
        ValidateNonNegative(outputSizeBytes, nameof(outputSizeBytes));

        InputPath = inputPath;
        OutputPath = outputPath;
        InputFormat = inputFormat;
        OutputFormat = outputFormat;
        InputSizeBytes = inputSizeBytes;
        OutputSizeBytes = outputSizeBytes;
        AppliedQuality = appliedQuality;
        Details = details;
    }

    public LocalPath InputPath { get; }

    public LocalPath OutputPath { get; }

    public ImageFormatKind InputFormat { get; }

    public ImageFormatKind OutputFormat { get; }

    public long InputSizeBytes { get; }

    public long OutputSizeBytes { get; }

    public ImageQuality? AppliedQuality { get; }

    public ImageProcessingDetails? Details { get; }
}
public sealed record ImageConvertRequest
{
    public ImageConvertRequest(LocalPath inputPath, LocalPath outputPath, ConversionProfile profile)
    {
        InputPath = inputPath;
        OutputPath = outputPath;
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
    }

    public LocalPath InputPath { get; }

    public LocalPath OutputPath { get; }

    public ConversionProfile Profile { get; }
}

public sealed record ImageConvertResult
{
    public ImageConvertResult(
        LocalPath inputPath,
        LocalPath outputPath,
        ImageFormatKind inputFormat,
        ImageFormatKind outputFormat,
        long inputSizeBytes,
        long outputSizeBytes,
        TransparencyProcessingResult transparency,
        ImageProcessingDetails? details = null)
    {
        ValidateKnownFormat(inputFormat, nameof(inputFormat));
        ValidateKnownFormat(outputFormat, nameof(outputFormat));
        ValidateNonNegative(inputSizeBytes, nameof(inputSizeBytes));
        ValidateNonNegative(outputSizeBytes, nameof(outputSizeBytes));

        InputPath = inputPath;
        OutputPath = outputPath;
        InputFormat = inputFormat;
        OutputFormat = outputFormat;
        InputSizeBytes = inputSizeBytes;
        OutputSizeBytes = outputSizeBytes;
        Transparency = transparency ?? throw new ArgumentNullException(nameof(transparency));
        Details = details;
    }

    public LocalPath InputPath { get; }

    public LocalPath OutputPath { get; }

    public ImageFormatKind InputFormat { get; }

    public ImageFormatKind OutputFormat { get; }

    public long InputSizeBytes { get; }

    public long OutputSizeBytes { get; }

    public TransparencyProcessingResult Transparency { get; }

    public ImageProcessingDetails? Details { get; }
}

public sealed record TransparencyProcessingResult
{
    public TransparencyProcessingResult(TransparencyOutcome outcome, RgbColor? backgroundColor)
    {
        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unsupported transparency outcome.");
        }

        if (outcome == TransparencyOutcome.Flattened && backgroundColor is null)
        {
            throw new ArgumentNullException(nameof(backgroundColor), "Flattened transparency requires the applied background color.");
        }

        if (outcome != TransparencyOutcome.Flattened && backgroundColor is not null)
        {
            throw new ArgumentException("Only flattened transparency can report a background color.", nameof(backgroundColor));
        }

        Outcome = outcome;
        BackgroundColor = backgroundColor;
    }

    public TransparencyOutcome Outcome { get; }

    public RgbColor? BackgroundColor { get; }
}

public enum TransparencyOutcome
{
    NotPresent,
    Preserved,
    Flattened
}

public sealed record ImageResizeRequest
{
    public ImageResizeRequest(LocalPath inputPath, LocalPath outputPath, ResolvedResizeSize targetSize, SameFormatEncodingPolicy encodingPolicy)
    {
        InputPath = inputPath;
        OutputPath = outputPath;
        TargetSize = targetSize ?? throw new ArgumentNullException(nameof(targetSize));
        EncodingPolicy = encodingPolicy ?? throw new ArgumentNullException(nameof(encodingPolicy));
    }

    public LocalPath InputPath { get; }
    public LocalPath OutputPath { get; }
    public ResolvedResizeSize TargetSize { get; }
    public SameFormatEncodingPolicy EncodingPolicy { get; }
}

public sealed record ImageResizeResult
{
    public ImageResizeResult(
        LocalPath inputPath,
        LocalPath outputPath,
        ImageFormatKind format,
        ImageSize inputSize,
        ImageSize outputSize,
        long inputSizeBytes,
        long outputSizeBytes)
    {
        ValidateKnownFormat(format, nameof(format));
        ValidateNonNegative(inputSizeBytes, nameof(inputSizeBytes));
        ValidateNonNegative(outputSizeBytes, nameof(outputSizeBytes));
        InputPath = inputPath;
        OutputPath = outputPath;
        Format = format;
        InputSize = inputSize;
        OutputSize = outputSize;
        InputSizeBytes = inputSizeBytes;
        OutputSizeBytes = outputSizeBytes;
    }

    public LocalPath InputPath { get; }
    public LocalPath OutputPath { get; }
    public ImageFormatKind Format { get; }
    public ImageSize InputSize { get; }
    public ImageSize OutputSize { get; }
    public long InputSizeBytes { get; }
    public long OutputSizeBytes { get; }
}

public sealed record ImageCropRequest
{
    public ImageCropRequest(LocalPath inputPath, LocalPath outputPath, CropRectangle cropArea, SameFormatEncodingPolicy encodingPolicy)
    {
        InputPath = inputPath;
        OutputPath = outputPath;
        CropArea = cropArea ?? throw new ArgumentNullException(nameof(cropArea));
        EncodingPolicy = encodingPolicy ?? throw new ArgumentNullException(nameof(encodingPolicy));
    }

    public LocalPath InputPath { get; }
    public LocalPath OutputPath { get; }
    public CropRectangle CropArea { get; }
    public SameFormatEncodingPolicy EncodingPolicy { get; }
}

public sealed record ImageCropResult
{
    public ImageCropResult(
        LocalPath inputPath,
        LocalPath outputPath,
        ImageFormatKind format,
        ImageSize inputSize,
        ImageSize outputSize,
        long inputSizeBytes,
        long outputSizeBytes)
    {
        ValidateKnownFormat(format, nameof(format));
        ValidateNonNegative(inputSizeBytes, nameof(inputSizeBytes));
        ValidateNonNegative(outputSizeBytes, nameof(outputSizeBytes));
        InputPath = inputPath;
        OutputPath = outputPath;
        Format = format;
        InputSize = inputSize;
        OutputSize = outputSize;
        InputSizeBytes = inputSizeBytes;
        OutputSizeBytes = outputSizeBytes;
    }

    public LocalPath InputPath { get; }
    public LocalPath OutputPath { get; }
    public ImageFormatKind Format { get; }
    public ImageSize InputSize { get; }
    public ImageSize OutputSize { get; }
    public long InputSizeBytes { get; }
    public long OutputSizeBytes { get; }
}

internal static class ImageProcessingContractValidation
{
    public static void ValidateKnownFormat(ImageFormatKind format, string parameterName)
    {
        if (!Enum.IsDefined(format) || format == ImageFormatKind.Unknown)
        {
            throw new ArgumentException("Image format must be known.", parameterName);
        }
    }

    public static void ValidatePositive(int value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value must be positive.");
        }
    }

    public static void ValidateNonNegative(long value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value cannot be negative.");
        }
    }

    public static void ValidatePositive(long value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value must be positive.");
        }
    }

    public static IReadOnlySet<ImageFormatKind> ValidateSameFormatSet(IReadOnlySet<ImageFormatKind> formats, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(formats, parameterName);
        var copy = formats.ToHashSet();
        if (copy.Count == 0 || copy.Contains(ImageFormatKind.Unknown))
        {
            throw new ArgumentException("At least one known same-format image format must be declared.", parameterName);
        }

        return copy;
    }
}
