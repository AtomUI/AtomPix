namespace AtomPix.Imaging.Abstractions.Processing;

using AtomPix.Core.Compression;
using AtomPix.Core.Conversion;
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
}

public sealed record ImageProcessorCapabilities
{
    public ImageProcessorCapabilities(
        IReadOnlySet<ImageFormatKind> supportedInputFormats,
        IReadOnlySet<OutputImageFormat> supportedOutputFormats,
        bool supportsMetadata,
        bool supportsAnimatedImages)
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
    }

    public IReadOnlySet<ImageFormatKind> SupportedInputFormats { get; }

    public IReadOnlySet<OutputImageFormat> SupportedOutputFormats { get; }

    public bool SupportsMetadata { get; }

    public bool SupportsAnimatedImages { get; }
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
        bool hasAlpha,
        bool isAnimated,
        int frameCount,
        bool hasMetadata)
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

        InputPath = inputPath;
        Format = format;
        Width = width;
        Height = height;
        FileSizeBytes = fileSizeBytes;
        HasAlpha = hasAlpha;
        IsAnimated = isAnimated;
        FrameCount = frameCount;
        HasMetadata = hasMetadata;
    }

    public LocalPath InputPath { get; }

    public ImageFormatKind Format { get; }

    public int Width { get; }

    public int Height { get; }

    public long FileSizeBytes { get; }

    public bool HasAlpha { get; }

    public bool IsAnimated { get; }

    public int FrameCount { get; }

    public bool HasMetadata { get; }
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
        bool resizeApplied,
        bool metadataRemoved,
        bool lossyOutput)
    {
        ValidatePositive(inputWidth, nameof(inputWidth));
        ValidatePositive(inputHeight, nameof(inputHeight));
        ValidatePositive(outputWidth, nameof(outputWidth));
        ValidatePositive(outputHeight, nameof(outputHeight));

        InputWidth = inputWidth;
        InputHeight = inputHeight;
        OutputWidth = outputWidth;
        OutputHeight = outputHeight;
        ResizeApplied = resizeApplied;
        MetadataRemoved = metadataRemoved;
        LossyOutput = lossyOutput;
    }

    public int InputWidth { get; }

    public int InputHeight { get; }

    public int OutputWidth { get; }

    public int OutputHeight { get; }

    public bool ResizeApplied { get; }

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
        ImageFormatKind outputFormat,
        long inputSizeBytes,
        long outputSizeBytes,
        ImageProcessingDetails? details = null)
    {
        ValidateKnownFormat(outputFormat, nameof(outputFormat));
        ValidateNonNegative(inputSizeBytes, nameof(inputSizeBytes));
        ValidateNonNegative(outputSizeBytes, nameof(outputSizeBytes));

        InputPath = inputPath;
        OutputPath = outputPath;
        OutputFormat = outputFormat;
        InputSizeBytes = inputSizeBytes;
        OutputSizeBytes = outputSizeBytes;
        Details = details;
    }

    public LocalPath InputPath { get; }

    public LocalPath OutputPath { get; }

    public ImageFormatKind OutputFormat { get; }

    public long InputSizeBytes { get; }

    public long OutputSizeBytes { get; }

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
        Details = details;
    }

    public LocalPath InputPath { get; }

    public LocalPath OutputPath { get; }

    public ImageFormatKind InputFormat { get; }

    public ImageFormatKind OutputFormat { get; }

    public long InputSizeBytes { get; }

    public long OutputSizeBytes { get; }

    public ImageProcessingDetails? Details { get; }
}
internal static class ImageProcessingContractValidation
{
    public static void ValidateKnownFormat(ImageFormatKind format, string parameterName)
    {
        if (format == ImageFormatKind.Unknown)
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
}
