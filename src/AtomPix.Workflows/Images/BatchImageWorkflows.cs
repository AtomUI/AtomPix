namespace AtomPix.Workflows.Images;

using AtomPix.Core.Compression;
using AtomPix.Core.Conversion;
using AtomPix.Core.Errors;
using AtomPix.Core.Jobs;
using AtomPix.Core.Output;
using AtomPix.Core.Results;
using AtomPix.Core.Resize;
using AtomPix.Core.ValueObjects;
using AtomPix.Imaging.Abstractions.Formats;
using AtomPix.Imaging.Abstractions.Processing;
using AtomPix.Workflows.Diagnostics;
using Microsoft.Extensions.Logging;

public sealed record BatchCompressRequest(
    IReadOnlyList<LocalPath> InputPaths,
    CompressionProfile Profile,
    OutputPolicy OutputPolicy);

public sealed record BatchCompressItemResult(ImageJobResult JobResult, ImageQuality? AppliedQuality);

public sealed record BatchCompressResult(
    BatchResult BatchResult,
    IReadOnlyList<BatchCompressItemResult> ItemResults,
    BatchProgressSnapshot FinalProgress);

public sealed class BatchCompressWorkflow
{
    private readonly ImageWorkflowServices _services;
    private readonly ILogger<BatchCompressWorkflow>? _logger;

    public BatchCompressWorkflow(ImageWorkflowServices services, ILogger<BatchCompressWorkflow>? logger = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _logger = logger;
    }

    public Task<OperationResult<BatchCompressResult>> ExecuteAsync(
        BatchCompressRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(request, progress: null, cancellationToken);

    public async Task<OperationResult<BatchCompressResult>> ExecuteAsync(
        BatchCompressRequest request,
        IProgress<BatchExecutionProgress<BatchCompressItemResult>>? progress,
        CancellationToken cancellationToken) =>
        await WorkflowDiagnostics.RunAsync(
            _logger,
            nameof(BatchCompressWorkflow),
            () => ExecuteCoreAsync(request, progress, cancellationToken)).ConfigureAwait(false);

    private async Task<OperationResult<BatchCompressResult>> ExecuteCoreAsync(
        BatchCompressRequest request,
        IProgress<BatchExecutionProgress<BatchCompressItemResult>>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.InputPaths);
        ArgumentNullException.ThrowIfNull(request.Profile);
        ArgumentNullException.ThrowIfNull(request.OutputPolicy);

        var start = await StartBatchAsync(
            ImageJobType.Compress,
            request.InputPaths,
            request.OutputPolicy,
            CompressionExtension,
            cancellationToken).ConfigureAwait(false);
        if (!start.Succeeded) return OperationResult<BatchCompressResult>.Failure(start.Error!);

        var execution = start.Value!;
        var publisher = new BatchProgressPublisher<BatchCompressItemResult>(
            progress,
            _logger,
            execution.BatchJob.Id,
            ImageJobType.Compress,
            execution.Plan);
        publisher.PublishInitial();
        var itemResults = new List<BatchCompressItemResult>();
        var canceled = false;
        AtomPixError? abortError = null;

        for (var index = 0; index < execution.Plan.Items.Count; index++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                canceled = true;
                break;
            }

            var planItem = execution.Plan.Items[index];
            var job = execution.BatchJob.Items[index];
            var probe = await _services.ValidateInputForSingleFrameProcessingAsync(planItem.InputPath, cancellationToken).ConfigureAwait(false);
            if (!probe.Succeeded)
            {
                if (WorkflowHelpers.IsCanceled(probe.Error)) job.MarkCanceled(probe.Error!, DateTimeOffset.UtcNow);
                else job.MarkFailed(probe.Error!, DateTimeOffset.UtcNow);
                var failed = new BatchCompressItemResult(WorkflowHelpers.ToResult(job, planItem.OutputPath, null, null), null);
                itemResults.Add(failed);
                publisher.PublishTerminal(index, job, failed);
                if (WorkflowHelpers.IsCanceled(probe.Error)) { canceled = true; break; }
                if (BatchWorkflowRuntime.ShouldAbortBatch(probe.Error)) { abortError = probe.Error; break; }
                continue;
            }

            if (!CompressImageWorkflow.TryGetCompressionOutputFormat(probe.Value!.Format, out var format)
                || !_services.SupportsOutputFormat(format))
            {
                var error = new AtomPixError(AtomPixErrorCode.UnsupportedOutputFormat, AtomPixErrorCategory.UnsupportedFormat, "Compression output format is not supported.");
                job.MarkFailed(error, DateTimeOffset.UtcNow);
                var failed = new BatchCompressItemResult(WorkflowHelpers.ToResult(job, planItem.OutputPath, probe.Value.FileSizeBytes, null), null);
                itemResults.Add(failed);
                publisher.PublishTerminal(index, job, failed);
                continue;
            }

            if (planItem.Decision == BatchOutputDecision.Skip)
            {
                job.MarkSkipped(planItem.OutputPath, planItem.Reason!, DateTimeOffset.UtcNow);
                var skipped = new BatchCompressItemResult(WorkflowHelpers.ToResult(job, planItem.OutputPath, probe.Value.FileSizeBytes, null), null);
                itemResults.Add(skipped);
                publisher.PublishTerminal(index, job, skipped);
                continue;
            }

            job.MarkRunning(DateTimeOffset.UtcNow);
            publisher.PublishRunning(index, job);
            var processed = await _services.ImageProcessor.CompressAsync(
                new ImageCompressRequest(planItem.InputPath, planItem.OutputPath, request.Profile),
                cancellationToken).ConfigureAwait(false);
            BatchWorkflowRuntime.CompleteImageJob(job, processed.Error, processed.Succeeded ? processed.Value!.OutputPath : null);
            var jobResult = WorkflowHelpers.ToResult(
                job,
                planItem.OutputPath,
                processed.Succeeded ? processed.Value!.InputSizeBytes : probe.Value.FileSizeBytes,
                processed.Succeeded && job.Status == ImageJobStatus.Succeeded ? processed.Value!.OutputSizeBytes : null);
            var itemResult = new BatchCompressItemResult(
                jobResult,
                processed.Succeeded && job.Status == ImageJobStatus.Succeeded ? processed.Value!.AppliedQuality : null);
            itemResults.Add(itemResult);
            publisher.PublishTerminal(index, job, itemResult);
            if (job.Status == ImageJobStatus.Canceled) { canceled = true; break; }
            if (BatchWorkflowRuntime.ShouldAbortBatch(processed.Error)) { abortError = processed.Error; break; }
        }

        var batch = FinishBatch(execution.BatchJob, itemResults.Select(item => item.JobResult).ToArray(), canceled, abortError);
        return OperationResult<BatchCompressResult>.Success(new BatchCompressResult(
            batch,
            itemResults.ToArray(),
            BatchProgressSnapshot.FromResults(batch.BatchId, batch.Type, batch.TotalCount, batch.Items, null)));
    }

    private async Task<OperationResult<BatchStartContext>> StartBatchAsync(
        ImageJobType type,
        IReadOnlyList<LocalPath> inputPaths,
        OutputPolicy outputPolicy,
        Func<LocalPath, string?> extension,
        CancellationToken cancellationToken) =>
        await BatchWorkflowRuntime.StartAsync(_services, type, inputPaths, outputPolicy, extension, cancellationToken).ConfigureAwait(false);

    private static string? CompressionExtension(LocalPath path) => Path.GetExtension(path.Value).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => ".jpg",
        ".png" => ".png",
        ".webp" => ".webp",
        { Length: > 1 } extension => extension,
        _ => null
    };

    private static BatchResult FinishBatch(BatchJob job, IReadOnlyList<ImageJobResult> results, bool canceled, AtomPixError? abortError) =>
        BatchWorkflowRuntime.Finish(job, results, canceled, abortError);
}

public sealed record BatchConvertRequest(
    IReadOnlyList<LocalPath> InputPaths,
    ConversionProfile Profile,
    OutputPolicy OutputPolicy);

public sealed record BatchConvertItemResult(
    ImageJobResult JobResult,
    TransparencyProcessingResult? Transparency);

public sealed record BatchConvertResult(
    BatchResult BatchResult,
    IReadOnlyList<BatchConvertItemResult> ItemResults,
    BatchProgressSnapshot FinalProgress);

public sealed class BatchConvertWorkflow
{
    private readonly ImageWorkflowServices _services;
    private readonly ILogger<BatchConvertWorkflow>? _logger;

    public BatchConvertWorkflow(ImageWorkflowServices services, ILogger<BatchConvertWorkflow>? logger = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _logger = logger;
    }

    public Task<OperationResult<BatchConvertResult>> ExecuteAsync(
        BatchConvertRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(request, progress: null, cancellationToken);

    public async Task<OperationResult<BatchConvertResult>> ExecuteAsync(
        BatchConvertRequest request,
        IProgress<BatchExecutionProgress<BatchConvertItemResult>>? progress,
        CancellationToken cancellationToken) =>
        await WorkflowDiagnostics.RunAsync(
            _logger,
            nameof(BatchConvertWorkflow),
            () => ExecuteCoreAsync(request, progress, cancellationToken)).ConfigureAwait(false);

    private async Task<OperationResult<BatchConvertResult>> ExecuteCoreAsync(
        BatchConvertRequest request,
        IProgress<BatchExecutionProgress<BatchConvertItemResult>>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.InputPaths);
        ArgumentNullException.ThrowIfNull(request.Profile);
        ArgumentNullException.ThrowIfNull(request.OutputPolicy);
        if (!_services.SupportsOutputFormat(request.Profile.OutputFormat))
        {
            return OperationResult<BatchConvertResult>.Failure(new AtomPixError(AtomPixErrorCode.UnsupportedOutputFormat, AtomPixErrorCategory.UnsupportedFormat, "Unsupported conversion output format."));
        }

        var extension = ConvertImageWorkflow.OutputExtension(request.Profile.OutputFormat);
        var start = await BatchWorkflowRuntime.StartAsync(
            _services,
            ImageJobType.Convert,
            request.InputPaths,
            request.OutputPolicy,
            _ => extension,
            cancellationToken).ConfigureAwait(false);
        if (!start.Succeeded) return OperationResult<BatchConvertResult>.Failure(start.Error!);

        var execution = start.Value!;
        var publisher = new BatchProgressPublisher<BatchConvertItemResult>(progress, _logger, execution.BatchJob.Id, ImageJobType.Convert, execution.Plan);
        publisher.PublishInitial();
        var itemResults = new List<BatchConvertItemResult>();
        var canceled = false;
        AtomPixError? abortError = null;

        for (var index = 0; index < execution.Plan.Items.Count; index++)
        {
            if (cancellationToken.IsCancellationRequested) { canceled = true; break; }
            var planItem = execution.Plan.Items[index];
            var job = execution.BatchJob.Items[index];
            var probe = await _services.ValidateInputForSingleFrameProcessingAsync(planItem.InputPath, cancellationToken).ConfigureAwait(false);
            if (!probe.Succeeded)
            {
                if (WorkflowHelpers.IsCanceled(probe.Error)) job.MarkCanceled(probe.Error!, DateTimeOffset.UtcNow);
                else job.MarkFailed(probe.Error!, DateTimeOffset.UtcNow);
                var failed = new BatchConvertItemResult(WorkflowHelpers.ToResult(job, planItem.OutputPath, null, null), null);
                itemResults.Add(failed);
                publisher.PublishTerminal(index, job, failed);
                if (WorkflowHelpers.IsCanceled(probe.Error)) { canceled = true; break; }
                if (BatchWorkflowRuntime.ShouldAbortBatch(probe.Error)) { abortError = probe.Error; break; }
                continue;
            }

            if (planItem.Decision == BatchOutputDecision.Skip)
            {
                job.MarkSkipped(planItem.OutputPath, planItem.Reason!, DateTimeOffset.UtcNow);
                var skipped = new BatchConvertItemResult(WorkflowHelpers.ToResult(job, planItem.OutputPath, probe.Value!.FileSizeBytes, null), null);
                itemResults.Add(skipped);
                publisher.PublishTerminal(index, job, skipped);
                continue;
            }

            job.MarkRunning(DateTimeOffset.UtcNow);
            publisher.PublishRunning(index, job);
            var processed = await _services.ImageProcessor.ConvertAsync(
                new ImageConvertRequest(planItem.InputPath, planItem.OutputPath, request.Profile),
                cancellationToken).ConfigureAwait(false);
            BatchWorkflowRuntime.CompleteImageJob(job, processed.Error, processed.Succeeded ? processed.Value!.OutputPath : null);
            var jobResult = WorkflowHelpers.ToResult(
                job,
                planItem.OutputPath,
                processed.Succeeded ? processed.Value!.InputSizeBytes : probe.Value!.FileSizeBytes,
                processed.Succeeded && job.Status == ImageJobStatus.Succeeded ? processed.Value!.OutputSizeBytes : null);
            var itemResult = new BatchConvertItemResult(
                jobResult,
                processed.Succeeded && job.Status == ImageJobStatus.Succeeded ? processed.Value!.Transparency : null);
            itemResults.Add(itemResult);
            publisher.PublishTerminal(index, job, itemResult);
            if (job.Status == ImageJobStatus.Canceled) { canceled = true; break; }
            if (BatchWorkflowRuntime.ShouldAbortBatch(processed.Error)) { abortError = processed.Error; break; }
        }

        var batch = BatchWorkflowRuntime.Finish(execution.BatchJob, itemResults.Select(item => item.JobResult).ToArray(), canceled, abortError);
        return OperationResult<BatchConvertResult>.Success(new BatchConvertResult(
            batch,
            itemResults.ToArray(),
            BatchProgressSnapshot.FromResults(batch.BatchId, batch.Type, batch.TotalCount, batch.Items, null)));
    }
}

public sealed record BatchResizeRequest(
    IReadOnlyList<LocalPath> InputPaths,
    ResizePolicy ResizePolicy,
    OutputPolicy OutputPolicy,
    SameFormatEncodingPolicy EncodingPolicy);

public sealed record BatchResizeItemResult(
    ImageJobResult JobResult,
    ImageFormatKind? Format,
    ImageSize? InputSize,
    ResolvedResizeSize? TargetSize,
    ImageSize? ActualOutputSize);

public sealed record BatchResizeResult(
    BatchResult BatchResult,
    IReadOnlyList<BatchResizeItemResult> ItemResults,
    BatchProgressSnapshot FinalProgress);

public sealed class BatchResizeWorkflow
{
    private readonly ImageWorkflowServices _services;
    private readonly ILogger<BatchResizeWorkflow>? _logger;

    public BatchResizeWorkflow(ImageWorkflowServices services, ILogger<BatchResizeWorkflow>? logger = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _logger = logger;
    }

    public Task<OperationResult<BatchResizeResult>> ExecuteAsync(
        BatchResizeRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(request, progress: null, cancellationToken);

    public async Task<OperationResult<BatchResizeResult>> ExecuteAsync(
        BatchResizeRequest request,
        IProgress<BatchExecutionProgress<BatchResizeItemResult>>? progress,
        CancellationToken cancellationToken) =>
        await WorkflowDiagnostics.RunAsync(
            _logger,
            nameof(BatchResizeWorkflow),
            () => ExecuteCoreAsync(request, progress, cancellationToken)).ConfigureAwait(false);

    private async Task<OperationResult<BatchResizeResult>> ExecuteCoreAsync(
        BatchResizeRequest request,
        IProgress<BatchExecutionProgress<BatchResizeItemResult>>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.InputPaths);
        ArgumentNullException.ThrowIfNull(request.ResizePolicy);
        ArgumentNullException.ThrowIfNull(request.OutputPolicy);
        ArgumentNullException.ThrowIfNull(request.EncodingPolicy);

        var start = await BatchWorkflowRuntime.StartAsync(
            _services,
            ImageJobType.Resize,
            request.InputPaths,
            request.OutputPolicy,
            SameFormatExtension,
            cancellationToken).ConfigureAwait(false);
        if (!start.Succeeded) return OperationResult<BatchResizeResult>.Failure(start.Error!);

        var execution = start.Value!;
        var publisher = new BatchProgressPublisher<BatchResizeItemResult>(progress, _logger, execution.BatchJob.Id, ImageJobType.Resize, execution.Plan);
        publisher.PublishInitial();
        var itemResults = new List<BatchResizeItemResult>();
        var canceled = false;
        AtomPixError? abortError = null;

        for (var index = 0; index < execution.Plan.Items.Count; index++)
        {
            if (cancellationToken.IsCancellationRequested) { canceled = true; break; }
            var planItem = execution.Plan.Items[index];
            var job = execution.BatchJob.Items[index];
            var probe = await _services.ValidateInputForSingleFrameProcessingAsync(planItem.InputPath, cancellationToken).ConfigureAwait(false);
            if (!probe.Succeeded)
            {
                if (WorkflowHelpers.IsCanceled(probe.Error)) job.MarkCanceled(probe.Error!, DateTimeOffset.UtcNow);
                else job.MarkFailed(probe.Error!, DateTimeOffset.UtcNow);
                var failed = new BatchResizeItemResult(WorkflowHelpers.ToResult(job, planItem.OutputPath, null, null), null, null, null, null);
                itemResults.Add(failed);
                publisher.PublishTerminal(index, job, failed);
                if (WorkflowHelpers.IsCanceled(probe.Error)) { canceled = true; break; }
                if (BatchWorkflowRuntime.ShouldAbortBatch(probe.Error)) { abortError = probe.Error; break; }
                continue;
            }

            var inputSize = new ImageSize(probe.Value!.Width, probe.Value.Height);
            ResolvedResizeSize targetSize;
            try
            {
                targetSize = request.ResizePolicy.Resolve(inputSize);
            }
            catch (Exception exception) when (exception is ArgumentException or OverflowException)
            {
                var error = new AtomPixError(AtomPixErrorCode.InvalidResizeOptions, AtomPixErrorCategory.Validation, "Resize options cannot be resolved for this image.");
                job.MarkFailed(error, DateTimeOffset.UtcNow);
                var failed = new BatchResizeItemResult(WorkflowHelpers.ToResult(job, planItem.OutputPath, probe.Value.FileSizeBytes, null), probe.Value.Format, inputSize, null, null);
                itemResults.Add(failed);
                publisher.PublishTerminal(index, job, failed);
                continue;
            }

            var capabilityError = ResizeImageWorkflow.ValidateResizeCapabilities(_services.ImageProcessor.Capabilities, probe.Value, targetSize);
            if (capabilityError is not null)
            {
                job.MarkFailed(capabilityError, DateTimeOffset.UtcNow);
                var failed = new BatchResizeItemResult(WorkflowHelpers.ToResult(job, planItem.OutputPath, probe.Value.FileSizeBytes, null), probe.Value.Format, inputSize, targetSize, null);
                itemResults.Add(failed);
                publisher.PublishTerminal(index, job, failed);
                continue;
            }

            if (planItem.Decision == BatchOutputDecision.Skip)
            {
                job.MarkSkipped(planItem.OutputPath, planItem.Reason!, DateTimeOffset.UtcNow);
                var skipped = new BatchResizeItemResult(WorkflowHelpers.ToResult(job, planItem.OutputPath, probe.Value.FileSizeBytes, null), probe.Value.Format, inputSize, targetSize, null);
                itemResults.Add(skipped);
                publisher.PublishTerminal(index, job, skipped);
                continue;
            }

            job.MarkRunning(DateTimeOffset.UtcNow);
            publisher.PublishRunning(index, job);
            var processed = await _services.ImageProcessor.ResizeAsync(
                new ImageResizeRequest(planItem.InputPath, planItem.OutputPath, targetSize, request.EncodingPolicy),
                cancellationToken).ConfigureAwait(false);

            ImageSize? actualOutputSize = null;
            if (processed.Succeeded
                && processed.Value!.Format == probe.Value.Format
                && processed.Value.OutputSize == targetSize.ToImageSize())
            {
                actualOutputSize = processed.Value.OutputSize;
                job.MarkSucceeded(processed.Value.OutputPath, DateTimeOffset.UtcNow);
            }
            else if (processed.Succeeded)
            {
                job.MarkFailed(WorkflowHelpers.ImageProcessingError(AtomPixErrorCode.ImageResizeFailed, "Resize output did not match the accepted plan."), DateTimeOffset.UtcNow);
            }
            else
            {
                BatchWorkflowRuntime.CompleteImageJob(job, processed.Error, null);
            }

            var jobResult = WorkflowHelpers.ToResult(
                job,
                planItem.OutputPath,
                processed.Succeeded ? processed.Value!.InputSizeBytes : probe.Value.FileSizeBytes,
                processed.Succeeded && job.Status == ImageJobStatus.Succeeded ? processed.Value!.OutputSizeBytes : null);
            var itemResult = new BatchResizeItemResult(jobResult, probe.Value.Format, inputSize, targetSize, actualOutputSize);
            itemResults.Add(itemResult);
            publisher.PublishTerminal(index, job, itemResult);
            if (job.Status == ImageJobStatus.Canceled) { canceled = true; break; }
            if (BatchWorkflowRuntime.ShouldAbortBatch(processed.Error)) { abortError = processed.Error; break; }
        }

        var batch = BatchWorkflowRuntime.Finish(execution.BatchJob, itemResults.Select(item => item.JobResult).ToArray(), canceled, abortError);
        return OperationResult<BatchResizeResult>.Success(new BatchResizeResult(
            batch,
            itemResults.ToArray(),
            BatchProgressSnapshot.FromResults(batch.BatchId, batch.Type, batch.TotalCount, batch.Items, null)));
    }

    private static string? SameFormatExtension(LocalPath path) => Path.GetExtension(path.Value).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => ".jpg",
        ".png" => ".png",
        ".webp" => ".webp",
        ".bmp" => ".bmp",
        { Length: > 1 } extension => extension,
        _ => null
    };
}

internal sealed record BatchStartContext(BatchOutputPlan Plan, BatchJob BatchJob);

internal static class BatchWorkflowRuntime
{
    public static async Task<OperationResult<BatchStartContext>> StartAsync(
        ImageWorkflowServices services,
        ImageJobType type,
        IReadOnlyList<LocalPath> inputPaths,
        OutputPolicy outputPolicy,
        Func<LocalPath, string?> extension,
        CancellationToken cancellationToken)
    {
        if (inputPaths.Count == 0) return OperationResult<BatchStartContext>.Failure(WorkflowHelpers.ValidationError("Input path list cannot be empty."));
        if (cancellationToken.IsCancellationRequested) return OperationResult<BatchStartContext>.Failure(WorkflowHelpers.CanceledError());

        var plan = services.CreateBatchOutputPlan(inputPaths, outputPolicy, extension);
        if (!plan.Succeeded) return OperationResult<BatchStartContext>.Failure(plan.Error!);
        var directories = await services.PrepareBatchOutputDirectoriesAsync(plan.Value!, cancellationToken).ConfigureAwait(false);
        if (!directories.Succeeded) return OperationResult<BatchStartContext>.Failure(directories.Error!);

        var createdAt = DateTimeOffset.UtcNow;
        var jobs = plan.Value!.Items
            .Select(item => new ImageJob(ImageJobId.New(), type, item.InputPath, createdAt))
            .ToArray();
        var batch = new BatchJob(BatchJobId.New(), type, jobs, createdAt);
        batch.MarkRunning(DateTimeOffset.UtcNow);
        return OperationResult<BatchStartContext>.Success(new BatchStartContext(plan.Value, batch));
    }

    public static void CompleteImageJob(ImageJob job, AtomPixError? error, LocalPath? outputPath)
    {
        if (error is null)
        {
            job.MarkSucceeded(outputPath!.Value, DateTimeOffset.UtcNow);
        }
        else if (WorkflowHelpers.IsCanceled(error))
        {
            job.MarkCanceled(error, DateTimeOffset.UtcNow);
        }
        else
        {
            job.MarkFailed(error, DateTimeOffset.UtcNow);
        }
    }

    public static bool ShouldAbortBatch(AtomPixError? error) =>
        error?.Code == AtomPixErrorCode.InsufficientDiskSpace;

    public static BatchResult Finish(
        BatchJob batchJob,
        IReadOnlyList<ImageJobResult> results,
        bool canceled,
        AtomPixError? abortError = null)
    {
        if (abortError is not null)
        {
            batchJob.Abort(abortError, DateTimeOffset.UtcNow);
        }
        else if (canceled)
        {
            batchJob.Cancel(WorkflowHelpers.CanceledError(), DateTimeOffset.UtcNow);
        }
        else
        {
            batchJob.CompleteNaturally(DateTimeOffset.UtcNow);
        }

        return new BatchResult(
            batchJob.Id,
            batchJob.Type,
            batchJob.Status,
            batchJob.Items.Count,
            results,
            batchJob.Error);
    }
}

internal sealed class BatchProgressPublisher<TItemResult>
    where TItemResult : class
{
    private readonly IProgress<BatchExecutionProgress<TItemResult>>? _progress;
    private readonly ILogger? _logger;
    private readonly BatchJobId _batchId;
    private readonly ImageJobType _type;
    private readonly BatchOutputPlan _outputPlan;
    private readonly List<ImageJobResult> _completed = [];
    private long _sequence;

    public BatchProgressPublisher(
        IProgress<BatchExecutionProgress<TItemResult>>? progress,
        ILogger? logger,
        BatchJobId batchId,
        ImageJobType type,
        BatchOutputPlan outputPlan)
    {
        _progress = progress;
        _logger = logger;
        _batchId = batchId;
        _type = type;
        _outputPlan = outputPlan ?? throw new ArgumentNullException(nameof(outputPlan));
    }

    public void PublishInitial() => Publish(null, null);

    public void PublishRunning(int index, ImageJob job) =>
        Publish(job.InputPath, new BatchItemProgress<TItemResult>(index, job.Id, job.InputPath, ImageJobStatus.Running, null));

    public void PublishTerminal(int index, ImageJob job, TItemResult result)
    {
        var jobResult = result switch
        {
            BatchCompressItemResult compress => compress.JobResult,
            BatchConvertItemResult convert => convert.JobResult,
            BatchResizeItemResult resize => resize.JobResult,
            _ => throw new InvalidOperationException("Unsupported batch item result type.")
        };
        _completed.Add(jobResult);
        Publish(null, new BatchItemProgress<TItemResult>(index, job.Id, job.InputPath, job.Status, result));
        WorkflowDiagnostics.LogBatchItemTerminal(_logger, index, job);
    }

    private void Publish(LocalPath? currentInput, BatchItemProgress<TItemResult>? changedItem)
    {
        if (_progress is null) return;
        var message = new BatchExecutionProgress<TItemResult>(
            ++_sequence,
            BatchProgressSnapshot.FromResults(_batchId, _type, _outputPlan.Items.Count, _completed, currentInput),
            changedItem,
            _outputPlan);
        try
        {
            _progress.Report(message);
        }
        catch (Exception)
        {
            // Observers cannot change the batch business result.
        }
    }
}
