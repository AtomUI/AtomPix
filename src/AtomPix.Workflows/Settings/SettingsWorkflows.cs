namespace AtomPix.Workflows.Settings;

using AtomPix.Core.Ports;
using AtomPix.Core.Results;
using AtomPix.Core.Settings;

public sealed record LoadSettingsRequest;
public sealed record LoadSettingsResult(AppSettings Settings);

public sealed class LoadSettingsWorkflow
{
    private readonly IAppSettingsStore? _settingsStore;
    private readonly AppSettingsStateCoordinator? _coordinator;

    public LoadSettingsWorkflow(IAppSettingsStore settingsStore)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
    }

    private LoadSettingsWorkflow(AppSettingsStateCoordinator coordinator)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
    }

    internal static LoadSettingsWorkflow ForApplicationState(AppSettingsStateCoordinator coordinator) =>
        new(coordinator);

    public async Task<OperationResult<LoadSettingsResult>> ExecuteAsync(LoadSettingsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = _coordinator is null
            ? await _settingsStore!.LoadAsync(cancellationToken).ConfigureAwait(false)
            : await _coordinator.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        return result.Succeeded
            ? OperationResult<LoadSettingsResult>.Success(new LoadSettingsResult(result.Value!))
            : OperationResult<LoadSettingsResult>.Failure(result.Error!);
    }
}

public sealed record SaveSettingsRequest(AppSettings Settings);
public sealed record SaveSettingsResult;

public sealed class SaveSettingsWorkflow
{
    private readonly IAppSettingsStore? _settingsStore;
    private readonly AppSettingsStateCoordinator? _coordinator;

    public SaveSettingsWorkflow(IAppSettingsStore settingsStore)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
    }

    private SaveSettingsWorkflow(AppSettingsStateCoordinator coordinator)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
    }

    internal static SaveSettingsWorkflow ForApplicationState(AppSettingsStateCoordinator coordinator) =>
        new(coordinator);

    public async Task<OperationResult<SaveSettingsResult>> ExecuteAsync(SaveSettingsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Settings);

        var result = _coordinator is null
            ? await _settingsStore!.SaveAsync(request.Settings, cancellationToken).ConfigureAwait(false)
            : await _coordinator.SaveAndPublishAsync(request.Settings, cancellationToken).ConfigureAwait(false);
        return result.Succeeded
            ? OperationResult<SaveSettingsResult>.Success(new SaveSettingsResult())
            : OperationResult<SaveSettingsResult>.Failure(result.Error!);
    }
}
