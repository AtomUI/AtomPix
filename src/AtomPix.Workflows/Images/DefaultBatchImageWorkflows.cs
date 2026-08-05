namespace AtomPix.Workflows.Images;

using AtomPix.Core.Ports;
using AtomPix.Core.Results;
using AtomPix.Core.ValueObjects;

public sealed record BatchCompressWithDefaultSettingsRequest(IReadOnlyList<LocalPath> InputPaths);
public sealed record BatchCompressWithDefaultSettingsResult(BatchCompressResult Result);

public sealed class BatchCompressWithDefaultSettingsWorkflow
{
    private readonly IAppSettingsStore _settingsStore;
    private readonly BatchCompressWorkflow _batchCompressWorkflow;

    public BatchCompressWithDefaultSettingsWorkflow(IAppSettingsStore settingsStore, BatchCompressWorkflow batchCompressWorkflow)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _batchCompressWorkflow = batchCompressWorkflow ?? throw new ArgumentNullException(nameof(batchCompressWorkflow));
    }

    public async Task<OperationResult<BatchCompressWithDefaultSettingsResult>> ExecuteAsync(BatchCompressWithDefaultSettingsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.InputPaths);

        var settings = await _settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (!settings.Succeeded)
        {
            return OperationResult<BatchCompressWithDefaultSettingsResult>.Failure(settings.Error!);
        }

        var result = await _batchCompressWorkflow.ExecuteAsync(
            new BatchCompressRequest(request.InputPaths, settings.Value!.DefaultCompressionProfile, settings.Value.DefaultOutputPolicy),
            cancellationToken).ConfigureAwait(false);

        return result.Succeeded
            ? OperationResult<BatchCompressWithDefaultSettingsResult>.Success(new BatchCompressWithDefaultSettingsResult(result.Value!))
            : OperationResult<BatchCompressWithDefaultSettingsResult>.Failure(result.Error!);
    }
}

public sealed record BatchConvertWithDefaultSettingsRequest(IReadOnlyList<LocalPath> InputPaths);
public sealed record BatchConvertWithDefaultSettingsResult(BatchConvertResult Result);

public sealed class BatchConvertWithDefaultSettingsWorkflow
{
    private readonly IAppSettingsStore _settingsStore;
    private readonly BatchConvertWorkflow _batchConvertWorkflow;

    public BatchConvertWithDefaultSettingsWorkflow(IAppSettingsStore settingsStore, BatchConvertWorkflow batchConvertWorkflow)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _batchConvertWorkflow = batchConvertWorkflow ?? throw new ArgumentNullException(nameof(batchConvertWorkflow));
    }

    public async Task<OperationResult<BatchConvertWithDefaultSettingsResult>> ExecuteAsync(BatchConvertWithDefaultSettingsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.InputPaths);

        var settings = await _settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (!settings.Succeeded)
        {
            return OperationResult<BatchConvertWithDefaultSettingsResult>.Failure(settings.Error!);
        }

        var result = await _batchConvertWorkflow.ExecuteAsync(
            new BatchConvertRequest(request.InputPaths, settings.Value!.DefaultConversionProfile, settings.Value.DefaultOutputPolicy),
            cancellationToken).ConfigureAwait(false);

        return result.Succeeded
            ? OperationResult<BatchConvertWithDefaultSettingsResult>.Success(new BatchConvertWithDefaultSettingsResult(result.Value!))
            : OperationResult<BatchConvertWithDefaultSettingsResult>.Failure(result.Error!);
    }
}
