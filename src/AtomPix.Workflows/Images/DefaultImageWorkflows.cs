namespace AtomPix.Workflows.Images;

using AtomPix.Core.Ports;
using AtomPix.Core.Results;
using AtomPix.Core.ValueObjects;

public sealed record CompressWithDefaultSettingsRequest(LocalPath InputPath);
public sealed record CompressWithDefaultSettingsResult(CompressImageResult Result);

public sealed class CompressWithDefaultSettingsWorkflow
{
    private readonly IAppSettingsStore _settingsStore;
    private readonly CompressImageWorkflow _compressWorkflow;

    public CompressWithDefaultSettingsWorkflow(IAppSettingsStore settingsStore, CompressImageWorkflow compressWorkflow)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _compressWorkflow = compressWorkflow ?? throw new ArgumentNullException(nameof(compressWorkflow));
    }

    public async Task<OperationResult<CompressWithDefaultSettingsResult>> ExecuteAsync(CompressWithDefaultSettingsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var settings = await _settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (!settings.Succeeded)
        {
            return OperationResult<CompressWithDefaultSettingsResult>.Failure(settings.Error!);
        }

        var result = await _compressWorkflow.ExecuteAsync(
            new CompressImageRequest(request.InputPath, settings.Value!.DefaultCompressionProfile, settings.Value.DefaultOutputPolicy),
            cancellationToken).ConfigureAwait(false);

        return result.Succeeded
            ? OperationResult<CompressWithDefaultSettingsResult>.Success(new CompressWithDefaultSettingsResult(result.Value!))
            : OperationResult<CompressWithDefaultSettingsResult>.Failure(result.Error!);
    }
}

public sealed record ConvertWithDefaultSettingsRequest(LocalPath InputPath);
public sealed record ConvertWithDefaultSettingsResult(ConvertImageResult Result);

public sealed class ConvertWithDefaultSettingsWorkflow
{
    private readonly IAppSettingsStore _settingsStore;
    private readonly ConvertImageWorkflow _convertWorkflow;

    public ConvertWithDefaultSettingsWorkflow(IAppSettingsStore settingsStore, ConvertImageWorkflow convertWorkflow)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _convertWorkflow = convertWorkflow ?? throw new ArgumentNullException(nameof(convertWorkflow));
    }

    public async Task<OperationResult<ConvertWithDefaultSettingsResult>> ExecuteAsync(ConvertWithDefaultSettingsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var settings = await _settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (!settings.Succeeded)
        {
            return OperationResult<ConvertWithDefaultSettingsResult>.Failure(settings.Error!);
        }

        var result = await _convertWorkflow.ExecuteAsync(
            new ConvertImageRequest(request.InputPath, settings.Value!.DefaultConversionProfile, settings.Value.DefaultOutputPolicy),
            cancellationToken).ConfigureAwait(false);

        return result.Succeeded
            ? OperationResult<ConvertWithDefaultSettingsResult>.Success(new ConvertWithDefaultSettingsResult(result.Value!))
            : OperationResult<ConvertWithDefaultSettingsResult>.Failure(result.Error!);
    }
}
