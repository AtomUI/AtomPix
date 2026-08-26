namespace AtomPix.Workflows.Images;

using AtomPix.Core.Compression;
using AtomPix.Core.Conversion;
using AtomPix.Core.Crop;
using AtomPix.Core.Errors;
using AtomPix.Core.Jobs;
using AtomPix.Core.Output;
using AtomPix.Core.Ports;
using AtomPix.Core.Results;
using AtomPix.Core.Resize;
using AtomPix.Core.ValueObjects;
using AtomPix.Imaging.Abstractions.Formats;
using AtomPix.Imaging.Abstractions.Processing;
using AtomPix.Workflows.Diagnostics;
using Microsoft.Extensions.Logging;

public sealed record OpenImageRequest(LocalPath InputPath);
public sealed record OpenImageResult(ImageProbeResult ProbeResult);

public sealed class OpenImageWorkflow
{
    private readonly IImageProcessor _imageProcessor;
    private readonly ILogger<OpenImageWorkflow>? _logger;

    public OpenImageWorkflow(IImageProcessor imageProcessor, ILogger<OpenImageWorkflow>? logger = null)
    {
        _imageProcessor = imageProcessor ?? throw new ArgumentNullException(nameof(imageProcessor));
        _logger = logger;
    }

    public Task<OperationResult<OpenImageResult>> ExecuteAsync(OpenImageRequest request, CancellationToken cancellationToken) =>
        WorkflowDiagnostics.RunAsync(_logger, nameof(OpenImageWorkflow), () => ExecuteCoreAsync(request, cancellationToken));

    private async Task<OperationResult<OpenImageResult>> ExecuteCoreAsync(OpenImageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await _imageProcessor.ProbeAsync(new ImageProbeRequest(request.InputPath), cancellationToken).ConfigureAwait(false);
        return result.Succeeded
            ? OperationResult<OpenImageResult>.Success(new OpenImageResult(result.Value!))
            : OperationResult<OpenImageResult>.Failure(result.Error!);
    }
}

public sealed record CreatePreviewRequest(LocalPath InputPath, int MaxPixelSize);
public sealed record CreatePreviewResult(ImagePreviewResult Preview);

public sealed class CreatePreviewWorkflow
{
    private readonly IImageProcessor _imageProcessor;
    private readonly ILogger<CreatePreviewWorkflow>? _logger;

    public CreatePreviewWorkflow(IImageProcessor imageProcessor, ILogger<CreatePreviewWorkflow>? logger = null)
    {
        _imageProcessor = imageProcessor ?? throw new ArgumentNullException(nameof(imageProcessor));
        _logger = logger;
    }

    public Task<OperationResult<CreatePreviewResult>> ExecuteAsync(CreatePreviewRequest request, CancellationToken cancellationToken) =>
        WorkflowDiagnostics.RunAsync(_logger, nameof(CreatePreviewWorkflow), () => ExecuteCoreAsync(request, cancellationToken));

    private async Task<OperationResult<CreatePreviewResult>> ExecuteCoreAsync(CreatePreviewRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.MaxPixelSize <= 0)
        {
            return OperationResult<CreatePreviewResult>.Failure(new AtomPixError(AtomPixErrorCode.InvalidResizeOptions, AtomPixErrorCategory.Validation, "Max pixel size must be greater than zero."));
        }

        var result = await _imageProcessor.CreatePreviewAsync(new ImagePreviewRequest(request.InputPath, request.MaxPixelSize), cancellationToken).ConfigureAwait(false);
        return result.Succeeded
            ? OperationResult<CreatePreviewResult>.Success(new CreatePreviewResult(result.Value!))
            : OperationResult<CreatePreviewResult>.Failure(result.Error!);
    }
}

public sealed record CompressImageRequest(LocalPath InputPath, CompressionProfile Profile, OutputPolicy OutputPolicy);
public sealed record CompressImageResult(ImageJobResult JobResult, ImageQuality? AppliedQuality, OutputWriteDisposition OutputDisposition);

public sealed class CompressImageWorkflow
{
    private readonly ImageWorkflowServices _services;
    private readonly ILogger<CompressImageWorkflow>? _logger;

    public CompressImageWorkflow(ImageWorkflowServices services, ILogger<CompressImageWorkflow>? logger = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _logger = logger;
    }

    public Task<OperationResult<CompressImageResult>> ExecuteAsync(CompressImageRequest request, CancellationToken cancellationToken) =>
        WorkflowDiagnostics.RunAsync(_logger, nameof(CompressImageWorkflow), () => ExecuteCoreAsync(request, cancellationToken));

    private async Task<OperationResult<CompressImageResult>> ExecuteCoreAsync(CompressImageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Profile);
        ArgumentNullException.ThrowIfNull(request.OutputPolicy);

        var probe = await _services.ValidateInputForSingleFrameProcessingAsync(request.InputPath, cancellationToken).ConfigureAwait(false);
        if (!probe.Succeeded) return OperationResult<CompressImageResult>.Failure(probe.Error!);

        if (!TryGetCompressionOutputFormat(probe.Value!.Format, out var compressionOutputFormat) || !_services.SupportsOutputFormat(compressionOutputFormat))
        {
            return OperationResult<CompressImageResult>.Failure(new AtomPixError(AtomPixErrorCode.UnsupportedOutputFormat, AtomPixErrorCategory.UnsupportedFormat, "Compression output format is not supported."));
        }

        var extension = GetCompressionExtension(request.InputPath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return OperationResult<CompressImageResult>.Failure(new AtomPixError(AtomPixErrorCode.UnsupportedOutputFormat, AtomPixErrorCategory.UnsupportedFormat, "Compression output extension cannot be resolved from input path."));
        }

        var inputSize = probe.Value!.FileSizeBytes;

        var jobId = ImageJobId.New();
        var output = await _services.ResolveOutputPathAsync(request.InputPath, request.OutputPolicy, extension, cancellationToken).ConfigureAwait(false);
        if (!output.Succeeded) return OperationResult<CompressImageResult>.Failure(output.Error!);

        var job = new ImageJob(jobId, ImageJobType.Compress, request.InputPath, DateTimeOffset.UtcNow);

        if (output.Value!.Skipped)
        {
            var error = WorkflowHelpers.OutputExistsError(output.Value.Path.GetValueOrDefault());
            job.MarkSkipped(output.Value.Path.GetValueOrDefault(), error, DateTimeOffset.UtcNow);
            var skipped = WorkflowHelpers.ToResult(job, output.Value.Path, inputSize, null);
            return OperationResult<CompressImageResult>.Success(new CompressImageResult(skipped, null, output.Value.Disposition));
        }

        job.MarkRunning(DateTimeOffset.UtcNow);
        var compress = await _services.ImageProcessor.CompressAsync(new ImageCompressRequest(request.InputPath, output.Value.Path.GetValueOrDefault(), request.Profile), cancellationToken).ConfigureAwait(false);
        if (compress.Succeeded)
        {
            job.MarkSucceeded(compress.Value!.OutputPath, DateTimeOffset.UtcNow);
        }
        else if (WorkflowHelpers.IsCanceled(compress.Error))
        {
            job.MarkCanceled(compress.Error!, DateTimeOffset.UtcNow);
        }
        else
        {
            job.MarkFailed(compress.Error!, DateTimeOffset.UtcNow);
        }

        var jobResult = WorkflowHelpers.ToResult(
            job,
            compress.Succeeded ? compress.Value!.OutputPath : output.Value.Path,
            compress.Succeeded ? compress.Value!.InputSizeBytes : inputSize,
            compress.Succeeded ? compress.Value!.OutputSizeBytes : null);
        return OperationResult<CompressImageResult>.Success(new CompressImageResult(jobResult, compress.Succeeded ? compress.Value!.AppliedQuality : null, output.Value.Disposition));
    }

    private static string GetCompressionExtension(LocalPath inputPath) => Path.GetExtension(inputPath.Value);

    internal static bool TryGetCompressionOutputFormat(ImageFormatKind format, out OutputImageFormat outputFormat)
    {
        outputFormat = format switch
        {
            ImageFormatKind.Jpeg => OutputImageFormat.Jpeg,
            ImageFormatKind.Png => OutputImageFormat.Png,
            ImageFormatKind.WebP => OutputImageFormat.WebP,
            _ => default
        };
        return format is ImageFormatKind.Jpeg or ImageFormatKind.Png or ImageFormatKind.WebP;
    }
}

public sealed record ConvertImageRequest(LocalPath InputPath, ConversionProfile Profile, OutputPolicy OutputPolicy);
public sealed record ConvertImageResult(ImageJobResult JobResult, TransparencyProcessingResult? Transparency, OutputWriteDisposition OutputDisposition);

public sealed class ConvertImageWorkflow
{
    private readonly ImageWorkflowServices _services;
    private readonly ILogger<ConvertImageWorkflow>? _logger;

    public ConvertImageWorkflow(ImageWorkflowServices services, ILogger<ConvertImageWorkflow>? logger = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _logger = logger;
    }

    public Task<OperationResult<ConvertImageResult>> ExecuteAsync(ConvertImageRequest request, CancellationToken cancellationToken) =>
        WorkflowDiagnostics.RunAsync(_logger, nameof(ConvertImageWorkflow), () => ExecuteCoreAsync(request, cancellationToken));

    private async Task<OperationResult<ConvertImageResult>> ExecuteCoreAsync(ConvertImageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Profile);
        ArgumentNullException.ThrowIfNull(request.OutputPolicy);

        if (!_services.SupportsOutputFormat(request.Profile.OutputFormat))
        {
            return OperationResult<ConvertImageResult>.Failure(new AtomPixError(AtomPixErrorCode.UnsupportedOutputFormat, AtomPixErrorCategory.UnsupportedFormat, "Unsupported conversion output format."));
        }

        var probe = await _services.ValidateInputForSingleFrameProcessingAsync(request.InputPath, cancellationToken).ConfigureAwait(false);
        if (!probe.Succeeded) return OperationResult<ConvertImageResult>.Failure(probe.Error!);

        if (!TryGetOutputExtension(request.Profile.OutputFormat, out var extension))
        {
            return OperationResult<ConvertImageResult>.Failure(new AtomPixError(AtomPixErrorCode.UnsupportedOutputFormat, AtomPixErrorCategory.UnsupportedFormat, "Unsupported conversion output format."));
        }

        var inputSize = probe.Value!.FileSizeBytes;

        var jobId = ImageJobId.New();
        var output = await _services.ResolveOutputPathAsync(request.InputPath, request.OutputPolicy, extension, cancellationToken).ConfigureAwait(false);
        if (!output.Succeeded) return OperationResult<ConvertImageResult>.Failure(output.Error!);

        var job = new ImageJob(jobId, ImageJobType.Convert, request.InputPath, DateTimeOffset.UtcNow);

        if (output.Value!.Skipped)
        {
            var error = WorkflowHelpers.OutputExistsError(output.Value.Path.GetValueOrDefault());
            job.MarkSkipped(output.Value.Path.GetValueOrDefault(), error, DateTimeOffset.UtcNow);
            var skipped = WorkflowHelpers.ToResult(job, output.Value.Path, inputSize, null);
            return OperationResult<ConvertImageResult>.Success(new ConvertImageResult(skipped, null, output.Value.Disposition));
        }

        job.MarkRunning(DateTimeOffset.UtcNow);
        var convert = await _services.ImageProcessor.ConvertAsync(new ImageConvertRequest(request.InputPath, output.Value.Path.GetValueOrDefault(), request.Profile), cancellationToken).ConfigureAwait(false);
        if (convert.Succeeded)
        {
            job.MarkSucceeded(convert.Value!.OutputPath, DateTimeOffset.UtcNow);
        }
        else if (WorkflowHelpers.IsCanceled(convert.Error))
        {
            job.MarkCanceled(convert.Error!, DateTimeOffset.UtcNow);
        }
        else
        {
            job.MarkFailed(convert.Error!, DateTimeOffset.UtcNow);
        }

        var jobResult = WorkflowHelpers.ToResult(
            job,
            convert.Succeeded ? convert.Value!.OutputPath : output.Value.Path,
            convert.Succeeded ? convert.Value!.InputSizeBytes : inputSize,
            convert.Succeeded ? convert.Value!.OutputSizeBytes : null);
        return OperationResult<ConvertImageResult>.Success(new ConvertImageResult(jobResult, convert.Succeeded ? convert.Value!.Transparency : null, output.Value.Disposition));
    }

    internal static string OutputExtension(OutputImageFormat format) => TryGetOutputExtension(format, out var extension)
        ? extension
        : throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported output image format.");

    private static bool TryGetOutputExtension(OutputImageFormat format, out string extension)
    {
        extension = format switch
        {
            OutputImageFormat.Jpeg => ".jpg",
            OutputImageFormat.Png => ".png",
            OutputImageFormat.WebP => ".webp",
            _ => string.Empty
        };
        return extension.Length > 0;
    }
}

public sealed record ResizeImageRequest(
    LocalPath InputPath,
    ResizePolicy ResizePolicy,
    OutputPolicy OutputPolicy,
    SameFormatEncodingPolicy EncodingPolicy);

public sealed record ResizeImageResult(
    ImageJobResult JobResult,
    ImageFormatKind Format,
    ImageSize InputSize,
    ResolvedResizeSize TargetSize,
    ImageSize? ActualOutputSize,
    OutputWriteDisposition OutputDisposition);

public sealed class ResizeImageWorkflow
{
    private readonly ImageWorkflowServices _services;
    private readonly ILogger<ResizeImageWorkflow>? _logger;

    public ResizeImageWorkflow(ImageWorkflowServices services, ILogger<ResizeImageWorkflow>? logger = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _logger = logger;
    }

    public Task<OperationResult<ResizeImageResult>> ExecuteAsync(ResizeImageRequest request, CancellationToken cancellationToken) =>
        WorkflowDiagnostics.RunAsync(_logger, nameof(ResizeImageWorkflow), () => ExecuteCoreAsync(request, cancellationToken));

    private async Task<OperationResult<ResizeImageResult>> ExecuteCoreAsync(ResizeImageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ResizePolicy);
        ArgumentNullException.ThrowIfNull(request.OutputPolicy);
        ArgumentNullException.ThrowIfNull(request.EncodingPolicy);

        var probe = await _services.ValidateInputForSingleFrameProcessingAsync(request.InputPath, cancellationToken).ConfigureAwait(false);
        if (!probe.Succeeded) return OperationResult<ResizeImageResult>.Failure(probe.Error!);

        var inputSize = new ImageSize(probe.Value!.Width, probe.Value.Height);
        ResolvedResizeSize targetSize;
        try
        {
            targetSize = request.ResizePolicy.Resolve(inputSize);
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            return OperationResult<ResizeImageResult>.Failure(new AtomPixError(
                AtomPixErrorCode.InvalidResizeOptions,
                AtomPixErrorCategory.Validation,
                "Resize options cannot be resolved for this image."));
        }

        var capabilityError = ValidateResizeCapabilities(_services.ImageProcessor.Capabilities, probe.Value, targetSize);
        if (capabilityError is not null) return OperationResult<ResizeImageResult>.Failure(capabilityError);

        if (!TryGetSameFormatExtension(probe.Value.Format, out var extension))
        {
            return OperationResult<ResizeImageResult>.Failure(new AtomPixError(
                AtomPixErrorCode.UnsupportedOutputFormat,
                AtomPixErrorCategory.UnsupportedFormat,
                "Resize output format is not supported."));
        }

        var output = await _services.ResolveOutputPathAsync(request.InputPath, request.OutputPolicy, extension, cancellationToken).ConfigureAwait(false);
        if (!output.Succeeded) return OperationResult<ResizeImageResult>.Failure(output.Error!);

        var job = new ImageJob(ImageJobId.New(), ImageJobType.Resize, request.InputPath, DateTimeOffset.UtcNow);
        if (output.Value!.Skipped)
        {
            var error = WorkflowHelpers.OutputExistsError(output.Value.Path.GetValueOrDefault());
            job.MarkSkipped(output.Value.Path.GetValueOrDefault(), error, DateTimeOffset.UtcNow);
            return OperationResult<ResizeImageResult>.Success(new ResizeImageResult(
                WorkflowHelpers.ToResult(job, output.Value.Path, probe.Value.FileSizeBytes, null),
                probe.Value.Format,
                inputSize,
                targetSize,
                null,
                output.Value.Disposition));
        }

        job.MarkRunning(DateTimeOffset.UtcNow);
        var resize = await _services.ImageProcessor.ResizeAsync(
            new ImageResizeRequest(request.InputPath, output.Value.Path.GetValueOrDefault(), targetSize, request.EncodingPolicy),
            cancellationToken).ConfigureAwait(false);

        ImageSize? actualOutputSize = null;
        if (resize.Succeeded)
        {
            var validOutput = resize.Value!.Format == probe.Value.Format
                && resize.Value.OutputSize.Width == targetSize.Width
                && resize.Value.OutputSize.Height == targetSize.Height;
            if (validOutput)
            {
                actualOutputSize = resize.Value.OutputSize;
                job.MarkSucceeded(resize.Value.OutputPath, DateTimeOffset.UtcNow);
            }
            else
            {
                job.MarkFailed(WorkflowHelpers.ImageProcessingError(AtomPixErrorCode.ImageResizeFailed, "Resize output did not match the accepted plan."), DateTimeOffset.UtcNow);
            }
        }
        else if (WorkflowHelpers.IsCanceled(resize.Error))
        {
            job.MarkCanceled(resize.Error!, DateTimeOffset.UtcNow);
        }
        else
        {
            job.MarkFailed(resize.Error!, DateTimeOffset.UtcNow);
        }

        var jobResult = WorkflowHelpers.ToResult(
            job,
            resize.Succeeded && job.Status == ImageJobStatus.Succeeded ? resize.Value!.OutputPath : output.Value.Path,
            resize.Succeeded ? resize.Value!.InputSizeBytes : probe.Value.FileSizeBytes,
            resize.Succeeded && job.Status == ImageJobStatus.Succeeded ? resize.Value!.OutputSizeBytes : null);
        return OperationResult<ResizeImageResult>.Success(new ResizeImageResult(
            jobResult,
            probe.Value.Format,
            inputSize,
            targetSize,
            actualOutputSize,
            output.Value.Disposition));
    }

    internal static AtomPixError? ValidateResizeCapabilities(
        ImageProcessorCapabilities capabilities,
        ImageProbeResult probe,
        ResolvedResizeSize targetSize)
    {
        var resize = capabilities.Resize;
        if (resize is null || !resize.SupportedSameFormatFormats.Contains(probe.Format))
        {
            return new AtomPixError(AtomPixErrorCode.UnsupportedInputFormat, AtomPixErrorCategory.UnsupportedFormat, "Input format does not support same-format resize.");
        }

        var pixels = checked((long)targetSize.Width * targetSize.Height);
        if (targetSize.Width > resize.MaxWidth
            || targetSize.Height > resize.MaxHeight
            || pixels > resize.MaxPixelCount
            || targetSize.Width > capabilities.Resources.MaxOutputWidth
            || targetSize.Height > capabilities.Resources.MaxOutputHeight
            || pixels > capabilities.Resources.MaxOutputPixelCount)
        {
            return new AtomPixError(AtomPixErrorCode.ImageDimensionsExceedLimit, AtomPixErrorCategory.Validation, "Requested resize dimensions exceed image processor limits.");
        }

        return null;
    }

    internal static bool TryGetSameFormatExtension(ImageFormatKind format, out string extension)
    {
        extension = format switch
        {
            ImageFormatKind.Jpeg => ".jpg",
            ImageFormatKind.Png => ".png",
            ImageFormatKind.WebP => ".webp",
            ImageFormatKind.Bmp => ".bmp",
            _ => string.Empty
        };
        return extension.Length > 0;
    }
}

public sealed record CropImageRequest(
    LocalPath InputPath,
    CropRectangle CropArea,
    OutputPolicy OutputPolicy,
    SameFormatEncodingPolicy EncodingPolicy);

public sealed record CropImageResult(
    ImageJobResult JobResult,
    ImageFormatKind Format,
    ImageSize InputSize,
    CropRectangle CropArea,
    ImageSize? ActualOutputSize,
    OutputWriteDisposition OutputDisposition);

public sealed class CropImageWorkflow
{
    private readonly ImageWorkflowServices _services;
    private readonly ILogger<CropImageWorkflow>? _logger;

    public CropImageWorkflow(ImageWorkflowServices services, ILogger<CropImageWorkflow>? logger = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _logger = logger;
    }

    public Task<OperationResult<CropImageResult>> ExecuteAsync(CropImageRequest request, CancellationToken cancellationToken) =>
        WorkflowDiagnostics.RunAsync(_logger, nameof(CropImageWorkflow), () => ExecuteCoreAsync(request, cancellationToken));

    private async Task<OperationResult<CropImageResult>> ExecuteCoreAsync(CropImageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.CropArea);
        ArgumentNullException.ThrowIfNull(request.OutputPolicy);
        ArgumentNullException.ThrowIfNull(request.EncodingPolicy);

        var probe = await _services.ValidateInputForSingleFrameProcessingAsync(request.InputPath, cancellationToken).ConfigureAwait(false);
        if (!probe.Succeeded) return OperationResult<CropImageResult>.Failure(probe.Error!);

        var inputSize = new ImageSize(probe.Value!.Width, probe.Value.Height);
        var cropValidation = CropRules.ValidateCropRectangle(inputSize, request.CropArea);
        if (!cropValidation.Succeeded) return OperationResult<CropImageResult>.Failure(cropValidation.Error!);

        var capabilityError = ValidateCropCapabilities(_services.ImageProcessor.Capabilities, probe.Value);
        if (capabilityError is not null) return OperationResult<CropImageResult>.Failure(capabilityError);

        if (!ResizeImageWorkflow.TryGetSameFormatExtension(probe.Value.Format, out var extension))
        {
            return OperationResult<CropImageResult>.Failure(new AtomPixError(
                AtomPixErrorCode.UnsupportedOutputFormat,
                AtomPixErrorCategory.UnsupportedFormat,
                "Crop output format is not supported."));
        }

        var output = await _services.ResolveOutputPathAsync(request.InputPath, request.OutputPolicy, extension, cancellationToken).ConfigureAwait(false);
        if (!output.Succeeded) return OperationResult<CropImageResult>.Failure(output.Error!);

        var job = new ImageJob(ImageJobId.New(), ImageJobType.Crop, request.InputPath, DateTimeOffset.UtcNow);
        if (output.Value!.Skipped)
        {
            var error = WorkflowHelpers.OutputExistsError(output.Value.Path.GetValueOrDefault());
            job.MarkSkipped(output.Value.Path.GetValueOrDefault(), error, DateTimeOffset.UtcNow);
            return OperationResult<CropImageResult>.Success(new CropImageResult(
                WorkflowHelpers.ToResult(job, output.Value.Path, probe.Value.FileSizeBytes, null),
                probe.Value.Format,
                inputSize,
                request.CropArea,
                null,
                output.Value.Disposition));
        }

        job.MarkRunning(DateTimeOffset.UtcNow);
        var crop = await _services.ImageProcessor.CropAsync(
            new ImageCropRequest(request.InputPath, output.Value.Path.GetValueOrDefault(), request.CropArea, request.EncodingPolicy),
            cancellationToken).ConfigureAwait(false);

        ImageSize? actualOutputSize = null;
        if (crop.Succeeded)
        {
            var validOutput = crop.Value!.Format == probe.Value.Format
                && crop.Value.OutputSize.Width == request.CropArea.Width
                && crop.Value.OutputSize.Height == request.CropArea.Height;
            if (validOutput)
            {
                actualOutputSize = crop.Value.OutputSize;
                job.MarkSucceeded(crop.Value.OutputPath, DateTimeOffset.UtcNow);
            }
            else
            {
                job.MarkFailed(WorkflowHelpers.ImageProcessingError(AtomPixErrorCode.ImageCropFailed, "Crop output did not match the accepted plan."), DateTimeOffset.UtcNow);
            }
        }
        else if (WorkflowHelpers.IsCanceled(crop.Error))
        {
            job.MarkCanceled(crop.Error!, DateTimeOffset.UtcNow);
        }
        else
        {
            job.MarkFailed(crop.Error!, DateTimeOffset.UtcNow);
        }

        var jobResult = WorkflowHelpers.ToResult(
            job,
            crop.Succeeded && job.Status == ImageJobStatus.Succeeded ? crop.Value!.OutputPath : output.Value.Path,
            crop.Succeeded ? crop.Value!.InputSizeBytes : probe.Value.FileSizeBytes,
            crop.Succeeded && job.Status == ImageJobStatus.Succeeded ? crop.Value!.OutputSizeBytes : null);
        return OperationResult<CropImageResult>.Success(new CropImageResult(
            jobResult,
            probe.Value.Format,
            inputSize,
            request.CropArea,
            actualOutputSize,
            output.Value.Disposition));
    }

    private static AtomPixError? ValidateCropCapabilities(ImageProcessorCapabilities capabilities, ImageProbeResult probe)
    {
        var crop = capabilities.Crop;
        if (crop is null || !crop.SupportedSameFormatFormats.Contains(probe.Format))
        {
            return new AtomPixError(AtomPixErrorCode.UnsupportedInputFormat, AtomPixErrorCategory.UnsupportedFormat, "Input format does not support same-format crop.");
        }

        var pixels = checked((long)probe.Width * probe.Height);
        if (probe.Width > crop.MaxInputWidth
            || probe.Height > crop.MaxInputHeight
            || pixels > crop.MaxInputPixelCount)
        {
            return new AtomPixError(AtomPixErrorCode.ImageDimensionsExceedLimit, AtomPixErrorCategory.Validation, "Input dimensions exceed crop limits.");
        }

        return null;
    }
}

public sealed class ImageWorkflowServices
{
    private readonly IFileSystemService _fileSystem;
    private readonly BatchOutputPlanner _batchOutputPlanner;

    public ImageWorkflowServices(
        IImageProcessor imageProcessor,
        IFileSystemService fileSystem)
    {
        ImageProcessor = imageProcessor ?? throw new ArgumentNullException(nameof(imageProcessor));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _batchOutputPlanner = new BatchOutputPlanner(_fileSystem);
    }

    public IImageProcessor ImageProcessor { get; }

    public OperationResult<BatchOutputPlan> CreateBatchOutputPlan(
        IReadOnlyList<LocalPath> inputPaths,
        OutputPolicy outputPolicy,
        Func<LocalPath, string?> outputExtension) =>
        _batchOutputPlanner.CreatePlan(inputPaths, outputPolicy, outputExtension);

    public Task<OperationResult> PrepareBatchOutputDirectoriesAsync(
        BatchOutputPlan plan,
        CancellationToken cancellationToken) =>
        _batchOutputPlanner.PrepareOutputDirectoriesAsync(plan, cancellationToken);


    public bool SupportsOutputFormat(OutputImageFormat outputFormat) =>
        ImageProcessor.Capabilities.SupportedOutputFormats.Contains(outputFormat);

    public async Task<OperationResult<ImageProbeResult>> ValidateInputForSingleFrameProcessingAsync(LocalPath inputPath, CancellationToken cancellationToken)
    {
        var fileSize = await _fileSystem.GetFileSizeAsync(inputPath, cancellationToken).ConfigureAwait(false);
        if (!fileSize.Succeeded)
        {
            return OperationResult<ImageProbeResult>.Failure(fileSize.Error!);
        }

        var resources = ImageProcessor.Capabilities.Resources;
        if (fileSize.Value > resources.MaxInputFileSizeBytes)
        {
            return OperationResult<ImageProbeResult>.Failure(ResourceLimitError(
                AtomPixErrorCode.InputFileTooLarge,
                "Input image file exceeds the image processor limit.",
                "InputFileSizeBytes",
                fileSize.Value,
                resources.MaxInputFileSizeBytes));
        }

        var probe = await ImageProcessor.ProbeAsync(new ImageProbeRequest(inputPath), cancellationToken).ConfigureAwait(false);
        if (!probe.Succeeded)
        {
            return OperationResult<ImageProbeResult>.Failure(probe.Error!);
        }

        var pixelCount = checked((long)probe.Value!.Width * probe.Value.Height);
        if (probe.Value.Width > resources.MaxInputWidth
            || probe.Value.Height > resources.MaxInputHeight
            || pixelCount > resources.MaxInputPixelCount)
        {
            return OperationResult<ImageProbeResult>.Failure(new AtomPixError(
                AtomPixErrorCode.ImageDimensionsExceedLimit,
                AtomPixErrorCategory.Validation,
                "Input image dimensions exceed the image processor limit.",
                new Dictionary<string, string>
                {
                    ["ResourceKind"] = "InputDimensions",
                    ["ActualWidth"] = probe.Value.Width.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["ActualHeight"] = probe.Value.Height.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["ActualPixelCount"] = pixelCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["MaximumWidth"] = resources.MaxInputWidth.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["MaximumHeight"] = resources.MaxInputHeight.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["MaximumPixelCount"] = resources.MaxInputPixelCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
                }));
        }

        if (!ImageProcessor.Capabilities.SupportedInputFormats.Contains(probe.Value.Format))
        {
            return OperationResult<ImageProbeResult>.Failure(new AtomPixError(AtomPixErrorCode.UnsupportedInputFormat, AtomPixErrorCategory.UnsupportedFormat, "Input image format is not supported."));
        }

        if (probe.Value.IsAnimated && !ImageProcessor.Capabilities.SupportsAnimatedImages)
        {
            return OperationResult<ImageProbeResult>.Failure(new AtomPixError(AtomPixErrorCode.UnsupportedInputFormat, AtomPixErrorCategory.UnsupportedFormat, "Animated or multi-frame images are not supported by this workflow."));
        }

        return OperationResult<ImageProbeResult>.Success(new ImageProbeResult(
            probe.Value.InputPath,
            probe.Value.Format,
            probe.Value.Width,
            probe.Value.Height,
            fileSize.Value,
            probe.Value.HasAlphaChannel,
            probe.Value.HasTransparency,
            probe.Value.IsAnimated,
            probe.Value.FrameCount,
            probe.Value.HasMetadata,
            probe.Value.HasColorProfile));
    }
    public async Task<OperationResult<ResolvedOutputPath>> ResolveOutputPathAsync(LocalPath inputPath, OutputPolicy policy, string extension, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return OperationResult<ResolvedOutputPath>.Failure(new AtomPixError(AtomPixErrorCode.UnsupportedOutputFormat, AtomPixErrorCategory.UnsupportedFormat, "Output extension cannot be empty."));
        }

        var directory = BatchOutputPlanner.ResolveOutputDirectory(inputPath, policy.LocationPolicy);
        var baseName = _fileSystem.GetFileNameWithoutExtension(inputPath);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            return OperationResult<ResolvedOutputPath>.Failure(WorkflowHelpers.ValidationError("Input path must contain a file name."));
        }

        var outputStem = BatchOutputPlanner.ExpandSingleStem(policy.NamingPolicy, baseName);
        var desired = _fileSystem.Combine(directory, outputStem + NormalizeExtension(extension));
        if (policy.OverwritePolicy == OverwritePolicy.Overwrite && _fileSystem.PathsEqual(inputPath, desired))
        {
            return OperationResult<ResolvedOutputPath>.Failure(new AtomPixError(
                AtomPixErrorCode.OutputPathConflictsWithInput,
                AtomPixErrorCategory.Validation,
                "Output path cannot overwrite an input image.",
                new Dictionary<string, string>
                {
                    ["InputPath"] = inputPath.Value,
                    ["OutputPath"] = desired.Value
                }));
        }

        ResolvedOutputPath resolved;
        var desiredExistsOrIsInput = _fileSystem.FileExists(desired) || _fileSystem.PathsEqual(inputPath, desired);
        if (!desiredExistsOrIsInput)
        {
            resolved = new ResolvedOutputPath(desired, false, OutputWriteDisposition.Created);
        }
        else
        {
            resolved = policy.OverwritePolicy switch
            {
                OverwritePolicy.Skip => new ResolvedOutputPath(desired, true, OutputWriteDisposition.SkippedExisting),
                OverwritePolicy.Overwrite => new ResolvedOutputPath(desired, false, OutputWriteDisposition.Overwritten),
                OverwritePolicy.AutoRename => new ResolvedOutputPath(FindAvailablePath(desired), false, OutputWriteDisposition.AutoRenamed),
                _ => throw new InvalidOperationException("Unsupported overwrite policy passed Core validation.")
            };
        }

        if (!resolved.Skipped)
        {
            var create = await _fileSystem.CreateDirectoryAsync(directory, cancellationToken).ConfigureAwait(false);
            if (!create.Succeeded) return OperationResult<ResolvedOutputPath>.Failure(create.Error!);
        }

        return OperationResult<ResolvedOutputPath>.Success(resolved);
    }

    private LocalPath FindAvailablePath(LocalPath desired)
    {
        var index = 1;
        var candidate = _fileSystem.BuildIndexedPath(desired, index);
        while (_fileSystem.FileExists(candidate))
        {
            index++;
            candidate = _fileSystem.BuildIndexedPath(desired, index);
        }

        return candidate;
    }

    private static string NormalizeExtension(string extension) => extension.StartsWith(".", StringComparison.Ordinal) ? extension : "." + extension;

    private static AtomPixError ResourceLimitError(
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
}

public sealed record ResolvedOutputPath(LocalPath? Path, bool Skipped, OutputWriteDisposition Disposition);

internal static class WorkflowHelpers
{
    public static BatchJobStatus DeriveBatchStatus(IReadOnlyList<ImageJobResult> results)
    {
        if (results.Count == 0) return BatchJobStatus.Failed;
        if (results.Any(r => r.Status == ImageJobStatus.Canceled)) return BatchJobStatus.Canceled;
        if (results.All(r => r.Status == ImageJobStatus.Failed)) return BatchJobStatus.Failed;
        if (results.All(r => r.Status is ImageJobStatus.Succeeded or ImageJobStatus.Skipped))
        {
            return BatchJobStatus.Succeeded;
        }

        return BatchJobStatus.PartiallySucceeded;
    }

    public static AtomPixError ValidationError(string message) =>
        new(AtomPixErrorCode.InvalidInputPath, AtomPixErrorCategory.Validation, message);

    public static AtomPixError CanceledError() =>
        new(AtomPixErrorCode.OperationCanceled, AtomPixErrorCategory.Cancellation, "Operation canceled.");

    public static bool IsCanceled(AtomPixError? error) =>
        error?.Code == AtomPixErrorCode.OperationCanceled || error?.Category == AtomPixErrorCategory.Cancellation;

    public static AtomPixError OutputExistsError(LocalPath outputPath) =>
        new(
            AtomPixErrorCode.OutputFileAlreadyExists,
            AtomPixErrorCategory.FileSystem,
            "Output file already exists and the current policy is Skip.",
            new Dictionary<string, string> { ["OutputPath"] = outputPath.Value });

    public static AtomPixError ImageProcessingError(AtomPixErrorCode code, string message) =>
        new(code, AtomPixErrorCategory.ImageProcessing, message);

    public static ImageJobResult ToResult(
        ImageJob job,
        LocalPath? outputPath,
        long? inputSizeBytes,
        long? outputSizeBytes) =>
        new(
            job.Id,
            job.Type,
            job.InputPath,
            outputPath,
            job.Status,
            inputSizeBytes,
            outputSizeBytes,
            job.Error);

    public static ImageJobResult CanceledJob(ImageJobType type, LocalPath inputPath) =>
        new(ImageJobId.New(), type, inputPath, null, ImageJobStatus.Canceled, null, null, CanceledError());
}


