namespace AtomPix.Workflows.Settings;

using AtomPix.Core.Errors;
using AtomPix.Core.Ports;
using AtomPix.Core.Results;
using AtomPix.Core.Settings;
using AtomUI.City.State;

public abstract record AppSettingsState
{
    private AppSettingsState()
    {
    }

    public sealed record Uninitialized : AppSettingsState
    {
        public static Uninitialized Instance { get; } = new();

        private Uninitialized()
        {
        }
    }

    public sealed record Ready(AppSettings Value) : AppSettingsState
    {
        public AppSettings Value { get; } = Value ?? throw new ArgumentNullException(nameof(Value));
    }

    public sealed record Faulted(AtomPixError Error) : AppSettingsState
    {
        public AtomPixError Error { get; } = Error ?? throw new ArgumentNullException(nameof(Error));
    }
}

public interface IAtomPixApplicationState
{
    IReadOnlyState<AppSettingsState> AppSettings { get; }
}

internal sealed class AppSettingsStateCoordinator : IAtomPixApplicationState, IDisposable
{
    internal const string OwnerModule = "AtomPix.Workflows";

    private static readonly StateKey<AppSettingsState> SettingsKey =
        new("AtomPix.Settings");

    private readonly IAppSettingsStore _store;
    private readonly IStateRegistry _registry;
    private readonly IApplicationState _applicationState;
    private readonly IApplicationStateWriterFactory _writerFactory;
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    private readonly object _initializationGate = new();
    private IApplicationStateWriter? _writer;
    private IReadOnlyState<AppSettingsState>? _settings;
    private Task<OperationResult<AppSettings>>? _initializationTask;
    private bool _disposed;

    public AppSettingsStateCoordinator(
        IAppSettingsStore store,
        IStateRegistry registry,
        IApplicationState applicationState,
        IApplicationStateWriterFactory writerFactory)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _applicationState = applicationState ?? throw new ArgumentNullException(nameof(applicationState));
        _writerFactory = writerFactory ?? throw new ArgumentNullException(nameof(writerFactory));
    }

    public IReadOnlyState<AppSettingsState> AppSettings =>
        _settings ?? throw new InvalidOperationException(
            "AtomPix settings state has not been registered by the Workflows module.");

    public void EnsureRegistered()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_writer is not null)
        {
            return;
        }

        _registry.Add(StateDefinition.Create(
            SettingsKey,
            AppSettingsState.Uninitialized.Instance,
            StateLifetime.Application,
            StateAccessPolicy.OwnerWrite,
            StateSnapshotPolicy.Transient,
            schemaVersion: 1,
            ownerModule: OwnerModule));
        _writer = _writerFactory.CreateWriter(StateWriteAuthority.Module(OwnerModule));
        _settings = _applicationState.Get(SettingsKey);
    }

    public Task<OperationResult<AppSettings>> EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureRegistered();

        lock (_initializationGate)
        {
            return _initializationTask ??= LoadAndPublishAsync(cancellationToken);
        }
    }

    public Task<OperationResult<AppSettings>> RetryLoadAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureRegistered();

        lock (_initializationGate)
        {
            _initializationTask = LoadAndPublishAsync(cancellationToken);
            return _initializationTask;
        }
    }

    public async Task<OperationResult> SaveAndPublishAsync(
        AppSettings candidate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureRegistered();

        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await _store.SaveAsync(candidate, cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                return result;
            }

            // The Store is the persistent source of truth. Publishing the exact immutable
            // candidate happens only after the durable write succeeds.
            _writer!.Set(SettingsKey, new AppSettingsState.Ready(candidate));
            return OperationResult.Success();
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _mutationGate.Dispose();
    }

    private async Task<OperationResult<AppSettings>> LoadAndPublishAsync(
        CancellationToken cancellationToken)
    {
        // Loads and saves share one mutation gate. Otherwise an older disk read can
        // publish after a successful save and make the in-memory projection regress.
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            AppSettingsState state = result.Succeeded
                ? new AppSettingsState.Ready(result.Value!)
                : new AppSettingsState.Faulted(result.Error!);
            _writer!.Set(SettingsKey, state);
            return result;
        }
        finally
        {
            _mutationGate.Release();
        }
    }
}
