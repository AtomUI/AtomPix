namespace AtomPix.Desktop.Localization;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using AtomUI.City.Core.Threading;
using AtomUI.City.Localization;
using CityLocalizedText = AtomUI.City.Localization.ILocalizedText;

public sealed class AtomPixLocalizedTextProjection : IAtomPixLocalizedText, IDisposable
{
    private static readonly LocalizationLookupContext LookupContext =
        new(moduleId: AtomPixLocalization.ModuleId);
    private static readonly IReadOnlyDictionary<string, string> EmptySnapshot =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());

    private readonly ILocalizationService _city;
    private readonly IUiDispatcher _dispatcher;
    private readonly object _gate = new();
    private readonly Dictionary<string, CityLocalizedText> _handles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _pending = new(StringComparer.Ordinal);
    private ILocalizationScopeLease? _scopeLease;
    private IReadOnlyDictionary<string, string> _snapshot = EmptySnapshot;
    private CultureInfo _currentCulture;
    private long _revision;
    private long _pendingRevision = -1;
    private bool _initialized;
    private bool _disposed;

    public AtomPixLocalizedTextProjection(
        ILocalizationService city,
        IUiDispatcher dispatcher)
    {
        _city = city ?? throw new ArgumentNullException(nameof(city));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _currentCulture = city.CurrentCulture;
        _revision = city.CultureRevision;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? LanguageChanged;

    public CultureInfo CurrentCulture => _currentCulture;

    public long Revision => Interlocked.Read(ref _revision);

    public async ValueTask InitializeAsync(
        IEnumerable<string> resourceKeys,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resourceKeys);
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_gate)
        {
            if (_initialized)
            {
                return;
            }
        }

        var handles = new List<CityLocalizedText>();
        ILocalizationScopeLease? scopeLease = _city.ActivateScope(LookupContext);
        try
        {
            foreach (var key in resourceKeys.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                handles.Add(await _city.CreateTextAsync(key, LookupContext, cancellationToken).ConfigureAwait(false));
            }

            await RefreshToOneRevisionAsync(handles, cancellationToken).ConfigureAwait(false);
            var snapshot = CreateSnapshot(handles);

            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_initialized)
                {
                    return;
                }

                foreach (var handle in handles)
                {
                    _handles.Add(handle.Key, handle);
                    handle.Changed += HandleTextChanged;
                }

                _snapshot = snapshot;
                _currentCulture = _city.CurrentCulture;
                _revision = _city.CultureRevision;
                _scopeLease = scopeLease;
                scopeLease = null;
                _initialized = true;
                handles.Clear();
            }
        }
        finally
        {
            foreach (var handle in handles)
            {
                handle.Dispose();
            }

            scopeLease?.Dispose();
        }
    }

    public string GetString(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var snapshot = Volatile.Read(ref _snapshot);
        return snapshot.TryGetValue(key, out var value) ? value : Missing(key);
    }

    public string Format(string key, params object[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return string.Format(CurrentCulture, GetString(key), arguments);
    }

    public void Dispose()
    {
        CityLocalizedText[] handles;
        ILocalizationScopeLease? scopeLease;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            handles = _handles.Values.ToArray();
            _handles.Clear();
            _pending.Clear();
            scopeLease = _scopeLease;
            _scopeLease = null;
        }

        foreach (var handle in handles)
        {
            handle.Changed -= HandleTextChanged;
            handle.Dispose();
        }

        scopeLease?.Dispose();
    }

    private async ValueTask RefreshToOneRevisionAsync(
        IReadOnlyList<CityLocalizedText> handles,
        CancellationToken cancellationToken)
    {
        while (handles.Count > 0)
        {
            var revision = _city.CultureRevision;
            if (handles.All(handle => handle.Revision == revision)
                && revision == _city.CultureRevision)
            {
                return;
            }

            foreach (var handle in handles)
            {
                await handle.RefreshAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static IReadOnlyDictionary<string, string> CreateSnapshot(
        IEnumerable<CityLocalizedText> handles) =>
        new ReadOnlyDictionary<string, string>(
            handles.ToDictionary(
                handle => handle.Key,
                handle => handle.IsMissing ? Missing(handle.Key) : handle.Value,
                StringComparer.Ordinal));

    private void HandleTextChanged(object? sender, LocalizedTextChangedEventArgs args)
    {
        if (sender is not CityLocalizedText handle)
        {
            return;
        }

        IReadOnlyDictionary<string, string>? nextSnapshot = null;
        CultureInfo? nextCulture = null;
        lock (_gate)
        {
            if (_disposed || !_initialized || args.Revision < _revision)
            {
                return;
            }

            if (_pendingRevision != args.Revision)
            {
                _pendingRevision = args.Revision;
                _pending.Clear();
            }

            _pending[handle.Key] = args.IsMissing ? Missing(handle.Key) : args.Value;
            if (_pending.Count == _handles.Count)
            {
                nextSnapshot = new ReadOnlyDictionary<string, string>(
                    new Dictionary<string, string>(_pending, StringComparer.Ordinal));
                nextCulture = args.Culture;
                _pending.Clear();
            }
        }

        if (nextSnapshot is not null && nextCulture is not null)
        {
            _dispatcher.InvokeAsync(() => Publish(nextSnapshot, nextCulture, args.Revision))
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }
    }

    private void Publish(
        IReadOnlyDictionary<string, string> snapshot,
        CultureInfo culture,
        long revision)
    {
        lock (_gate)
        {
            if (_disposed || revision < _revision)
            {
                return;
            }

            Volatile.Write(ref _snapshot, snapshot);
            _currentCulture = culture;
            Interlocked.Exchange(ref _revision, revision);
        }

        OnPropertyChanged(nameof(CurrentCulture));
        OnPropertyChanged(nameof(Revision));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string Missing(string key) => $"[[{key}]]";

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
