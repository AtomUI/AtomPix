namespace AtomPix.Workflows.Settings;

using AtomPix.Core.Ports;
using AtomPix.Core.Results;
using AtomPix.Core.Settings;

public sealed record LoadSettingsRequest;
public sealed record LoadSettingsResult(AppSettings Settings);

public sealed class LoadSettingsWorkflow
{
    private readonly IAppSettingsStore _settingsStore;

    public LoadSettingsWorkflow(IAppSettingsStore settingsStore)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
    }

    public async Task<OperationResult<LoadSettingsResult>> ExecuteAsync(LoadSettingsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await _settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        return result.Succeeded
            ? OperationResult<LoadSettingsResult>.Success(new LoadSettingsResult(result.Value!))
            : OperationResult<LoadSettingsResult>.Failure(result.Error!);
    }
}

public sealed record SaveSettingsRequest(AppSettings Settings);
public sealed record SaveSettingsResult;

public sealed class SaveSettingsWorkflow
{
    private readonly IAppSettingsStore _settingsStore;

    public SaveSettingsWorkflow(IAppSettingsStore settingsStore)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
    }

    public async Task<OperationResult<SaveSettingsResult>> ExecuteAsync(SaveSettingsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Settings);

        var result = await _settingsStore.SaveAsync(request.Settings, cancellationToken).ConfigureAwait(false);
        return result.Succeeded
            ? OperationResult<SaveSettingsResult>.Success(new SaveSettingsResult())
            : OperationResult<SaveSettingsResult>.Failure(result.Error!);
    }
}
