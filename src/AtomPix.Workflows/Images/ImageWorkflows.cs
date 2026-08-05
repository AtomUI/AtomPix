namespace AtomPix.Workflows.Images;

using AtomPix.Core.Compression;
using AtomPix.Core.Conversion;
using AtomPix.Core.Errors;
using AtomPix.Core.Jobs;
using AtomPix.Core.Licensing;
using AtomPix.Core.Output;
using AtomPix.Core.Ports;
using AtomPix.Core.Results;
using AtomPix.Core.ValueObjects;
using AtomPix.Imaging.Abstractions.Formats;
using AtomPix.Imaging.Abstractions.Processing;

public sealed record OpenImageRequest(LocalPath InputPath);
public sealed record OpenImageResult(ImageProbeResult ProbeResult);

public sealed class OpenImageWorkflow
{
    private readonly IImageProcessor _imageProcessor;

    public OpenImageWorkflow(IImageProcessor imageProcessor) => _imageProcessor = imageProcessor ?? throw new ArgumentNullException(nameof(imageProcessor));

    public async Task<OperationResult<OpenImageResult>> ExecuteAsync(OpenImageRequest request, CancellationToken cancellationToken)
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

    public CreatePreviewWorkflow(IImageProcessor imageProcessor) => _imageProcessor = imageProcessor ?? throw new ArgumentNullException(nameof(imageProcessor));

    public async Task<OperationResult<CreatePreviewResult>> ExecuteAsync(CreatePreviewRequest request, CancellationToken cancellationToken)
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
public sealed record CompressImageResult(ImageJobResult JobResult);

public sealed class CompressImageWorkflow
{
    private readonly ImageWorkflowServices _services;

    public CompressImageWorkflow(ImageWorkflowServices services) => _services = services ?? throw new ArgumentNullException(nameof(services));

    public async Task<OperationResult<CompressImageResult>> ExecuteAsync(CompressImageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Profile);
        ArgumentNullException.ThrowIfNull(request.OutputPolicy);

        var access = await _services.CheckAccessAsync(FeatureId.SingleCompress, cancellationToken).ConfigureAwait(false);
        if (!access.Succeeded) return OperationResult<CompressImageResult>.Failure(access.Error!);

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

        var inputSize = await _services.GetInputSizeAsync(request.InputPath, cancellationToken).ConfigureAwait(false);
        if (!inputSize.Succeeded) return OperationResult<CompressImageResult>.Failure(inputSize.Error!);

        var jobId = ImageJobId.New();
        var output = await _services.ResolveOutputPathAsync(request.InputPath, request.OutputPolicy, extension, cancellationToken).ConfigureAwait(false);
        if (!output.Succeeded) return OperationResult<CompressImageResult>.Failure(output.Error!);

        if (output.Value!.Skipped)
        {
            var skipped = new ImageJobResult(jobId, ImageJobType.Compress, request.InputPath, output.Value.Path, ImageJobStatus.Skipped, inputSize.Value, null, null);
            return OperationResult<CompressImageResult>.Success(new CompressImageResult(skipped));
        }

        var compress = await _services.ImageProcessor.CompressAsync(new ImageCompressRequest(request.InputPath, output.Value.Path.GetValueOrDefault(), request.Profile), cancellationToken).ConfigureAwait(false);
        var jobResult = compress.Succeeded
            ? new ImageJobResult(jobId, ImageJobType.Compress, request.InputPath, compress.Value!.OutputPath, ImageJobStatus.Succeeded, compress.Value.InputSizeBytes, compress.Value.OutputSizeBytes, null)
            : WorkflowHelpers.IsCanceled(compress.Error)
                ? new ImageJobResult(jobId, ImageJobType.Compress, request.InputPath, output.Value.Path, ImageJobStatus.Canceled, inputSize.Value, null, compress.Error)
                : new ImageJobResult(jobId, ImageJobType.Compress, request.InputPath, output.Value.Path, ImageJobStatus.Failed, inputSize.Value, null, compress.Error);
        return OperationResult<CompressImageResult>.Success(new CompressImageResult(jobResult));
    }

    private static string GetCompressionExtension(LocalPath inputPath) => Path.GetExtension(inputPath.Value);

    private static bool TryGetCompressionOutputFormat(ImageFormatKind format, out OutputImageFormat outputFormat)
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
public sealed record ConvertImageResult(ImageJobResult JobResult);

public sealed class ConvertImageWorkflow
{
    private readonly ImageWorkflowServices _services;

    public ConvertImageWorkflow(ImageWorkflowServices services) => _services = services ?? throw new ArgumentNullException(nameof(services));

    public async Task<OperationResult<ConvertImageResult>> ExecuteAsync(ConvertImageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Profile);
        ArgumentNullException.ThrowIfNull(request.OutputPolicy);

        var access = await _services.CheckAccessAsync(FeatureId.SingleConvert, cancellationToken).ConfigureAwait(false);
        if (!access.Succeeded) return OperationResult<ConvertImageResult>.Failure(access.Error!);

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

        var inputSize = await _services.GetInputSizeAsync(request.InputPath, cancellationToken).ConfigureAwait(false);
        if (!inputSize.Succeeded) return OperationResult<ConvertImageResult>.Failure(inputSize.Error!);

        var jobId = ImageJobId.New();
        var output = await _services.ResolveOutputPathAsync(request.InputPath, request.OutputPolicy, extension, cancellationToken).ConfigureAwait(false);
        if (!output.Succeeded) return OperationResult<ConvertImageResult>.Failure(output.Error!);

        if (output.Value!.Skipped)
        {
            var skipped = new ImageJobResult(jobId, ImageJobType.Convert, request.InputPath, output.Value.Path, ImageJobStatus.Skipped, inputSize.Value, null, null);
            return OperationResult<ConvertImageResult>.Success(new ConvertImageResult(skipped));
        }

        var convert = await _services.ImageProcessor.ConvertAsync(new ImageConvertRequest(request.InputPath, output.Value.Path.GetValueOrDefault(), request.Profile), cancellationToken).ConfigureAwait(false);
        var jobResult = convert.Succeeded
            ? new ImageJobResult(jobId, ImageJobType.Convert, request.InputPath, convert.Value!.OutputPath, ImageJobStatus.Succeeded, convert.Value.InputSizeBytes, convert.Value.OutputSizeBytes, null)
            : WorkflowHelpers.IsCanceled(convert.Error)
                ? new ImageJobResult(jobId, ImageJobType.Convert, request.InputPath, output.Value.Path, ImageJobStatus.Canceled, inputSize.Value, null, convert.Error)
                : new ImageJobResult(jobId, ImageJobType.Convert, request.InputPath, output.Value.Path, ImageJobStatus.Failed, inputSize.Value, null, convert.Error);
        return OperationResult<ConvertImageResult>.Success(new ConvertImageResult(jobResult));
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

public sealed record BatchCompressRequest(IReadOnlyList<LocalPath> InputPaths, CompressionProfile Profile, OutputPolicy OutputPolicy);
public sealed record BatchCompressResult(BatchResult BatchResult, BatchProgressSnapshot FinalProgress);

public sealed class BatchCompressWorkflow
{
    private readonly ImageWorkflowServices _services;
    private readonly CompressImageWorkflow _single;

    public BatchCompressWorkflow(ImageWorkflowServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _single = new CompressImageWorkflow(services);
    }

    public async Task<OperationResult<BatchCompressResult>> ExecuteAsync(BatchCompressRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.InputPaths);
        ArgumentNullException.ThrowIfNull(request.Profile);
        ArgumentNullException.ThrowIfNull(request.OutputPolicy);

        var access = await _services.CheckAccessAsync(FeatureId.BatchCompress, cancellationToken).ConfigureAwait(false);
        if (!access.Succeeded) return OperationResult<BatchCompressResult>.Failure(access.Error!);
        if (request.InputPaths.Count == 0) return OperationResult<BatchCompressResult>.Failure(WorkflowHelpers.ValidationError("Input path list cannot be empty."));
        if (cancellationToken.IsCancellationRequested) return OperationResult<BatchCompressResult>.Failure(WorkflowHelpers.CanceledError());

        var results = new List<ImageJobResult>();
        foreach (var path in request.InputPaths)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                results.Add(WorkflowHelpers.CanceledJob(ImageJobType.Compress, path));
                break;
            }

            var result = await _single.ExecuteAsync(new CompressImageRequest(path, request.Profile, request.OutputPolicy), cancellationToken).ConfigureAwait(false);
            if (result.Succeeded) results.Add(result.Value!.JobResult);
            else if (WorkflowHelpers.IsCanceled(result.Error)) results.Add(WorkflowHelpers.CanceledJob(ImageJobType.Compress, path));
            else results.Add(new ImageJobResult(ImageJobId.New(), ImageJobType.Compress, path, null, ImageJobStatus.Failed, null, null, result.Error));
        }

        var batch = new BatchResult(BatchJobId.New(), ImageJobType.Compress, WorkflowHelpers.DeriveBatchStatus(results), results, request.InputPaths.Count);
        var progress = BatchProgressSnapshot.FromResults(batch.BatchId, batch.Type, batch.TotalCount, batch.Items, currentInputPath: null);
        return OperationResult<BatchCompressResult>.Success(new BatchCompressResult(batch, progress));
    }
}

public sealed record BatchConvertRequest(IReadOnlyList<LocalPath> InputPaths, ConversionProfile Profile, OutputPolicy OutputPolicy);
public sealed record BatchConvertResult(BatchResult BatchResult, BatchProgressSnapshot FinalProgress);

public sealed class BatchConvertWorkflow
{
    private readonly ImageWorkflowServices _services;
    private readonly ConvertImageWorkflow _single;

    public BatchConvertWorkflow(ImageWorkflowServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _single = new ConvertImageWorkflow(services);
    }

    public async Task<OperationResult<BatchConvertResult>> ExecuteAsync(BatchConvertRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.InputPaths);
        ArgumentNullException.ThrowIfNull(request.Profile);
        ArgumentNullException.ThrowIfNull(request.OutputPolicy);

        var access = await _services.CheckAccessAsync(FeatureId.BatchConvert, cancellationToken).ConfigureAwait(false);
        if (!access.Succeeded) return OperationResult<BatchConvertResult>.Failure(access.Error!);
        if (request.InputPaths.Count == 0) return OperationResult<BatchConvertResult>.Failure(WorkflowHelpers.ValidationError("Input path list cannot be empty."));
        if (cancellationToken.IsCancellationRequested) return OperationResult<BatchConvertResult>.Failure(WorkflowHelpers.CanceledError());

        var results = new List<ImageJobResult>();
        foreach (var path in request.InputPaths)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                results.Add(WorkflowHelpers.CanceledJob(ImageJobType.Convert, path));
                break;
            }

            var result = await _single.ExecuteAsync(new ConvertImageRequest(path, request.Profile, request.OutputPolicy), cancellationToken).ConfigureAwait(false);
            if (result.Succeeded) results.Add(result.Value!.JobResult);
            else if (WorkflowHelpers.IsCanceled(result.Error)) results.Add(WorkflowHelpers.CanceledJob(ImageJobType.Convert, path));
            else results.Add(new ImageJobResult(ImageJobId.New(), ImageJobType.Convert, path, null, ImageJobStatus.Failed, null, null, result.Error));
        }

        var batch = new BatchResult(BatchJobId.New(), ImageJobType.Convert, WorkflowHelpers.DeriveBatchStatus(results), results, request.InputPaths.Count);
        var progress = BatchProgressSnapshot.FromResults(batch.BatchId, batch.Type, batch.TotalCount, batch.Items, currentInputPath: null);
        return OperationResult<BatchConvertResult>.Success(new BatchConvertResult(batch, progress));
    }
}

public sealed class ImageWorkflowServices
{
    private readonly ISubscriptionStore _subscriptionStore;
    private readonly IFeatureAccessPolicy _featureAccessPolicy;
    private readonly IFileSystemService _fileSystem;

    public ImageWorkflowServices(
        IImageProcessor imageProcessor,
        ISubscriptionStore subscriptionStore,
        IFeatureAccessPolicy featureAccessPolicy,
        IFileSystemService fileSystem)
    {
        ImageProcessor = imageProcessor ?? throw new ArgumentNullException(nameof(imageProcessor));
        _subscriptionStore = subscriptionStore ?? throw new ArgumentNullException(nameof(subscriptionStore));
        _featureAccessPolicy = featureAccessPolicy ?? throw new ArgumentNullException(nameof(featureAccessPolicy));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public IImageProcessor ImageProcessor { get; }

    public async Task<OperationResult> CheckAccessAsync(FeatureId feature, CancellationToken cancellationToken)
    {
        var subscription = await _subscriptionStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (!subscription.Succeeded) return OperationResult.Failure(subscription.Error!);
        var decision = _featureAccessPolicy.CanUse(feature, subscription.Value!);
        if (decision.Allowed) return OperationResult.Success();
        var code = decision.BlockReason == FeatureAccessBlockReason.SubscriptionExpired ? AtomPixErrorCode.SubscriptionExpired : AtomPixErrorCode.FeatureNotAvailable;
        return OperationResult.Failure(new AtomPixError(code, AtomPixErrorCategory.FeatureAccess, "Feature is not available for the current subscription."));
    }


    public bool SupportsOutputFormat(OutputImageFormat outputFormat) =>
        ImageProcessor.Capabilities.SupportedOutputFormats.Contains(outputFormat);

    public async Task<OperationResult<ImageProbeResult>> ValidateInputForSingleFrameProcessingAsync(LocalPath inputPath, CancellationToken cancellationToken)
    {
        var probe = await ImageProcessor.ProbeAsync(new ImageProbeRequest(inputPath), cancellationToken).ConfigureAwait(false);
        if (!probe.Succeeded)
        {
            return OperationResult<ImageProbeResult>.Failure(probe.Error!);
        }

        if (!ImageProcessor.Capabilities.SupportedInputFormats.Contains(probe.Value!.Format))
        {
            return OperationResult<ImageProbeResult>.Failure(new AtomPixError(AtomPixErrorCode.UnsupportedInputFormat, AtomPixErrorCategory.UnsupportedFormat, "Input image format is not supported."));
        }

        if (probe.Value.IsAnimated && !ImageProcessor.Capabilities.SupportsAnimatedImages)
        {
            return OperationResult<ImageProbeResult>.Failure(new AtomPixError(AtomPixErrorCode.UnsupportedInputFormat, AtomPixErrorCategory.UnsupportedFormat, "Animated or multi-frame images are not supported by this workflow."));
        }

        return probe;
    }
    public async Task<OperationResult<long>> GetInputSizeAsync(LocalPath inputPath, CancellationToken cancellationToken) =>
        await _fileSystem.GetFileSizeAsync(inputPath, cancellationToken).ConfigureAwait(false);

    public async Task<OperationResult<ResolvedOutputPath>> ResolveOutputPathAsync(LocalPath inputPath, OutputPolicy policy, string extension, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return OperationResult<ResolvedOutputPath>.Failure(new AtomPixError(AtomPixErrorCode.UnsupportedOutputFormat, AtomPixErrorCategory.UnsupportedFormat, "Output extension cannot be empty."));
        }

        var directory = ResolveOutputDirectory(inputPath, policy.LocationPolicy);
        var create = await _fileSystem.CreateDirectoryAsync(directory, cancellationToken).ConfigureAwait(false);
        if (!create.Succeeded) return OperationResult<ResolvedOutputPath>.Failure(create.Error!);

        var baseName = _fileSystem.GetFileNameWithoutExtension(inputPath);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            return OperationResult<ResolvedOutputPath>.Failure(WorkflowHelpers.ValidationError("Input path must contain a file name."));
        }

        if (policy.NamingPolicy.Mode == OutputNamingMode.AppendSuffix && !string.IsNullOrWhiteSpace(policy.NamingPolicy.Suffix))
        {
            baseName += policy.NamingPolicy.Suffix;
        }

        var desired = _fileSystem.Combine(directory, baseName + NormalizeExtension(extension));
        if (!_fileSystem.FileExists(desired)) return OperationResult<ResolvedOutputPath>.Success(new ResolvedOutputPath(desired, false));

        return policy.OverwritePolicy switch
        {
            OverwritePolicy.Skip => OperationResult<ResolvedOutputPath>.Success(new ResolvedOutputPath(desired, true)),
            OverwritePolicy.Overwrite => OperationResult<ResolvedOutputPath>.Success(new ResolvedOutputPath(desired, false)),
            OverwritePolicy.AutoRename => OperationResult<ResolvedOutputPath>.Success(new ResolvedOutputPath(FindAvailablePath(desired), false)),
            _ => OperationResult<ResolvedOutputPath>.Failure(WorkflowHelpers.ValidationError("Unsupported overwrite policy."))
        };
    }

    private static LocalPath ResolveOutputDirectory(LocalPath inputPath, OutputLocationPolicy policy)
    {
        return policy.Mode switch
        {
            OutputLocationMode.SameAsInput => new LocalPath(Path.GetDirectoryName(inputPath.Value) ?? "."),
            OutputLocationMode.Subfolder => new LocalPath(Path.Combine(Path.GetDirectoryName(inputPath.Value) ?? ".", policy.SubfolderName ?? "AtomPix_Output")),
            OutputLocationMode.CustomDirectory when !string.IsNullOrWhiteSpace(policy.CustomDirectory) => new LocalPath(policy.CustomDirectory),
            _ => new LocalPath(Path.GetDirectoryName(inputPath.Value) ?? ".")
        };
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
}

public sealed record ResolvedOutputPath(LocalPath? Path, bool Skipped);

internal static class WorkflowHelpers
{
    public static BatchJobStatus DeriveBatchStatus(IReadOnlyList<ImageJobResult> results)
    {
        if (results.Count == 0) return BatchJobStatus.Failed;
        if (results.Any(r => r.Status == ImageJobStatus.Canceled)) return BatchJobStatus.Canceled;
        if (results.All(r => r.Status == ImageJobStatus.Failed)) return BatchJobStatus.Failed;
        if (results.All(r => r.Status is ImageJobStatus.Succeeded or ImageJobStatus.Skipped))
        {
            return results.All(r => r.Status == ImageJobStatus.Succeeded) || results.All(r => r.Status == ImageJobStatus.Skipped)
                ? BatchJobStatus.Succeeded
                : BatchJobStatus.PartiallySucceeded;
        }

        return BatchJobStatus.PartiallySucceeded;
    }

    public static AtomPixError ValidationError(string message) =>
        new(AtomPixErrorCode.InvalidInputPath, AtomPixErrorCategory.Validation, message);

    public static AtomPixError CanceledError() =>
        new(AtomPixErrorCode.OperationCanceled, AtomPixErrorCategory.Cancellation, "Operation canceled.");

    public static bool IsCanceled(AtomPixError? error) =>
        error?.Code == AtomPixErrorCode.OperationCanceled || error?.Category == AtomPixErrorCategory.Cancellation;

    public static ImageJobResult CanceledJob(ImageJobType type, LocalPath inputPath) =>
        new(ImageJobId.New(), type, inputPath, null, ImageJobStatus.Canceled, null, null, CanceledError());
}


