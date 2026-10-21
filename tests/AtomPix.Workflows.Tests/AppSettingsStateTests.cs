namespace AtomPix.Workflows.Tests;

using AtomPix.Core.Errors;
using AtomPix.Core.Ports;
using AtomPix.Core.Results;
using AtomPix.Core.Settings;
using AtomPix.Workflows.DependencyInjection;
using AtomPix.Workflows.Settings;
using AtomUI.City.State;
using Microsoft.Extensions.DependencyInjection;

public sealed class AppSettingsStateTests
{
    [Fact]
    public async Task Initial_load_publishes_ready_and_is_shared_by_all_callers()
    {
        var store = new ControlledSettingsStore(AppSettings.Default);
        using var provider = CreateProvider(store);
        var coordinator = Register(provider);
        var load = provider.GetRequiredService<LoadSettingsWorkflow>();

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            load.ExecuteAsync(new LoadSettingsRequest(), CancellationToken.None)));

        Assert.All(results, result => Assert.True(result.Succeeded));
        Assert.Equal(1, store.LoadCount);
        var ready = Assert.IsType<AppSettingsState.Ready>(coordinator.AppSettings.Value);
        Assert.Same(AppSettings.Default, ready.Value);
    }

    [Fact]
    public async Task Initial_load_failure_is_a_recoverable_faulted_projection()
    {
        var error = new AtomPixError(
            AtomPixErrorCode.SettingsLoadFailed,
            AtomPixErrorCategory.Configuration,
            "Synthetic load failure.");
        var store = new ControlledSettingsStore(AppSettings.Default) { LoadError = error };
        using var provider = CreateProvider(store);
        var coordinator = Register(provider);

        var result = await provider.GetRequiredService<LoadSettingsWorkflow>()
            .ExecuteAsync(new LoadSettingsRequest(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Same(error, Assert.IsType<AppSettingsState.Faulted>(coordinator.AppSettings.Value).Error);
    }

    [Fact]
    public async Task Failed_store_write_does_not_publish_candidate()
    {
        var error = new AtomPixError(
            AtomPixErrorCode.SettingsSaveFailed,
            AtomPixErrorCategory.FileSystem,
            "Synthetic save failure.");
        var store = new ControlledSettingsStore(AppSettings.Default);
        using var provider = CreateProvider(store);
        var coordinator = Register(provider);
        await coordinator.EnsureLoadedAsync(CancellationToken.None);
        var version = coordinator.AppSettings.Version;
        store.SaveError = error;
        var candidate = CreateSettings("en-US");

        var result = await provider.GetRequiredService<SaveSettingsWorkflow>()
            .ExecuteAsync(new SaveSettingsRequest(candidate), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(version, coordinator.AppSettings.Version);
        Assert.Same(AppSettings.Default, Assert.IsType<AppSettingsState.Ready>(coordinator.AppSettings.Value).Value);
    }

    [Fact]
    public async Task Concurrent_saves_publish_in_durable_store_order()
    {
        var store = new ControlledSettingsStore(AppSettings.Default)
        {
            PauseFirstSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        using var provider = CreateProvider(store);
        var coordinator = Register(provider);
        await coordinator.EnsureLoadedAsync(CancellationToken.None);
        var first = CreateSettings("en-US");
        var second = CreateSettings("zh-TW");
        var save = provider.GetRequiredService<SaveSettingsWorkflow>();

        var firstTask = save.ExecuteAsync(new SaveSettingsRequest(first), CancellationToken.None);
        await store.FirstSaveEntered.Task;
        var secondTask = save.ExecuteAsync(new SaveSettingsRequest(second), CancellationToken.None);
        store.PauseFirstSave.SetResult();
        await Task.WhenAll(firstTask, secondTask);

        Assert.Equal([first, second], store.Saved);
        Assert.Same(second, store.Current);
        Assert.Same(second, Assert.IsType<AppSettingsState.Ready>(coordinator.AppSettings.Value).Value);
    }

    private static ServiceProvider CreateProvider(ControlledSettingsStore store)
    {
        var services = new ServiceCollection();
        services.AddState();
        services.AddSingleton<IAppSettingsStore>(store);
        services.AddAtomPixWorkflows();
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static AppSettings CreateSettings(string language) => new(
        AppSettings.Default.DefaultCompressionProfile,
        AppSettings.Default.DefaultConversionProfile,
        AppSettings.Default.DefaultSameFormatEncodingPolicy,
        AppSettings.Default.DefaultOutputPolicy,
        AppSettings.Default.ThemeMode,
        language,
        AppSettings.Default.RecentItems);

    private static AppSettingsStateCoordinator Register(ServiceProvider provider)
    {
        var coordinator = provider.GetRequiredService<AppSettingsStateCoordinator>();
        coordinator.EnsureRegistered();
        return coordinator;
    }

    private sealed class ControlledSettingsStore : IAppSettingsStore
    {
        private int _loadCount;
        private int _saveCount;

        public ControlledSettingsStore(AppSettings current) => Current = current;

        public AppSettings Current { get; private set; }

        public AtomPixError? LoadError { get; set; }

        public AtomPixError? SaveError { get; set; }

        public int LoadCount => Volatile.Read(ref _loadCount);

        public List<AppSettings> Saved { get; } = [];

        public TaskCompletionSource FirstSaveEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource? PauseFirstSave { get; init; }

        public Task<OperationResult<AppSettings>> LoadAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _loadCount);
            return Task.FromResult(LoadError is null
                ? OperationResult<AppSettings>.Success(Current)
                : OperationResult<AppSettings>.Failure(LoadError));
        }

        public async Task<OperationResult> SaveAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            var saveNumber = Interlocked.Increment(ref _saveCount);
            if (saveNumber == 1)
            {
                FirstSaveEntered.TrySetResult();
                if (PauseFirstSave is not null)
                {
                    await PauseFirstSave.Task.WaitAsync(cancellationToken);
                }
            }

            if (SaveError is not null)
            {
                return OperationResult.Failure(SaveError);
            }

            Saved.Add(settings);
            Current = settings;
            return OperationResult.Success();
        }
    }
}
