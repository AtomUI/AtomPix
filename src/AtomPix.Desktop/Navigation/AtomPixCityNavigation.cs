namespace AtomPix.Desktop.Navigation;

using System.Collections.Concurrent;
using AtomPix.Desktop.ViewModels;
using AtomPix.Desktop.Views;
using AtomUI.City.Core.Threading;
using AtomUI.City.Presentation;
using AtomUI.City.Routing;
using Microsoft.Extensions.DependencyInjection;

internal static class AtomPixRoutes
{
    internal const string Home = "home";
    internal const string BrowserOpen = "browser.open";
    internal const string BrowserResume = "browser.resume";
    internal const string Settings = "settings";

    internal static IReadOnlyList<RouteDescriptor> Descriptors { get; } =
    [
        Route(Home, "home", typeof(HomePageViewModel)),
        Route(BrowserOpen, "browser/open/{sessionKey}", typeof(ImageBrowserViewModel)),
        Route(BrowserResume, "browser/resume/{workspaceId}", typeof(ImageBrowserViewModel)),
        Route(Settings, "settings", typeof(SettingsPageViewModel)),
    ];

    private static RouteDescriptor Route(string id, string template, Type viewModelType) =>
        new(
            id,
            RouteDefinitionKind.Route,
            template,
            new ViewModelTargetDescriptor(viewModelType),
            outletName: "primary");
}

public sealed class BrowserNavigationSessionStore
{
    private static readonly TimeSpan DefaultSessionLifetime = TimeSpan.FromMinutes(2);
    private const int DefaultCapacity = 32;

    private readonly ConcurrentDictionary<Guid, Entry> _entries = new();
    private readonly object _syncRoot = new();
    private readonly int _capacity;
    private readonly TimeSpan _sessionLifetime;
    private readonly Func<DateTimeOffset> _utcNow;

    public BrowserNavigationSessionStore()
        : this(DefaultCapacity, DefaultSessionLifetime, static () => DateTimeOffset.UtcNow)
    {
    }

    internal BrowserNavigationSessionStore(
        int capacity,
        TimeSpan sessionLifetime,
        Func<DateTimeOffset> utcNow)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        if (sessionLifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(sessionLifetime));
        }

        _capacity = capacity;
        _sessionLifetime = sessionLifetime;
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
    }

    internal int Count
    {
        get
        {
            lock (_syncRoot)
            {
                return _entries.Count;
            }
        }
    }

    public Guid Add(BrowserNavigationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        lock (_syncRoot)
        {
            RemoveExpiredCore();
            if (_entries.Count >= _capacity)
            {
                var oldest = _entries.OrderBy(pair => pair.Value.CreatedAt).FirstOrDefault();
                if (oldest.Key != Guid.Empty)
                {
                    _entries.TryRemove(oldest.Key, out _);
                }
            }

            var key = Guid.NewGuid();
            if (!_entries.TryAdd(key, new Entry(context, _utcNow())))
            {
                throw new InvalidOperationException("Could not allocate a unique Browser navigation session.");
            }

            return key;
        }
    }

    public bool TryConsume(Guid key, out BrowserNavigationContext? context)
    {
        lock (_syncRoot)
        {
            context = null;
            if (!_entries.TryRemove(key, out var entry)
                || _utcNow() - entry.CreatedAt > _sessionLifetime)
            {
                return false;
            }

            context = entry.Context;
            return true;
        }
    }

    public void Discard(Guid key)
    {
        lock (_syncRoot)
        {
            _entries.TryRemove(key, out _);
        }
    }

    private void RemoveExpiredCore()
    {
        var cutoff = _utcNow() - _sessionLifetime;
        foreach (var pair in _entries)
        {
            if (pair.Value.CreatedAt < cutoff)
            {
                _entries.TryRemove(pair.Key, out _);
            }
        }
    }

    private sealed record Entry(BrowserNavigationContext Context, DateTimeOffset CreatedAt);
}

internal sealed class AtomPixViewCatalog
{
    private readonly IViewRegistry _registry;

    public AtomPixViewCatalog(IViewRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public void Register()
    {
        _registry.Register(View<HomePageViewModel, HomePageView>());
        _registry.Register(View<ImageBrowserViewModel, ImageBrowserView>());
        _registry.Register(View<SettingsPageViewModel, SettingsPageView>());
    }

    private static ViewDescriptor View<TViewModel, TView>()
        where TView : new() =>
        new(
            typeof(TViewModel),
            typeof(TView),
            viewKey: null,
            _ => new TView());
}

internal sealed class PreparedRoutePlanFactory : IRoutePresentationPlanFactory
{
    private readonly object _viewModel;
    private readonly IViewLocator _locator;
    private readonly ViewFactory _viewFactory;
    private readonly ViewBinder _binder;
    private readonly IUiDispatcher _dispatcher;
    private readonly Action<RoutePresentationPlanContext>? _beforeCreate;

    public PreparedRoutePlanFactory(
        object viewModel,
        IViewLocator locator,
        ViewFactory viewFactory,
        ViewBinder binder,
        IUiDispatcher dispatcher,
        Action<RoutePresentationPlanContext>? beforeCreate = null)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _viewFactory = viewFactory ?? throw new ArgumentNullException(nameof(viewFactory));
        _binder = binder ?? throw new ArgumentNullException(nameof(binder));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _beforeCreate = beforeCreate;
    }

    public async ValueTask<RouteOutletCommitPlan> CreateAsync(
        RoutePresentationPlanContext context,
        CancellationToken cancellationToken = default)
    {
        _beforeCreate?.Invoke(context);
        var expectedType = context.Navigation.Route.ViewModelTarget?.ViewModelType
            ?? throw new InvalidOperationException(
                $"Route '{context.Navigation.Route.RouteId}' has no ViewModel target.");
        if (!expectedType.IsInstanceOfType(_viewModel))
        {
            throw new InvalidOperationException(
                $"Prepared ViewModel '{_viewModel.GetType().FullName}' does not match route target '{expectedType.FullName}'.");
        }

        var descriptor = _locator.Locate(expectedType);
        var view = await _viewFactory.CreateAsync(descriptor, cancellationToken).ConfigureAwait(false);
        BoundViewHandle handle;
        var bindStarted = false;
        try
        {
            handle = await _dispatcher.InvokeAsync(
                () =>
                {
                    bindStarted = true;
                    return _binder.Bind(descriptor, view, _viewModel);
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // ViewBinder owns cleanup after binding starts. We only own a view when
            // dispatch was cancelled or failed before the callback began.
            if (!bindStarted && view is IDisposable disposable)
            {
                await _dispatcher.InvokeAsync(disposable.Dispose, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            throw;
        }

        var lease = _viewModel is HomePageViewModel or SettingsPageViewModel
            ? ViewModelLease.EntryOwned(_viewModel)
            : ViewModelLease.Borrowed(_viewModel);

        return RouteOutletCommitPlan.Replace(
            context.OutletName,
            handle,
            lease,
            context.Navigation.Route.RouteId);
    }
}

public sealed class AtomPixCityNavigator : IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRoutePresentationCoordinator _coordinator;
    private readonly IViewLocator _locator;
    private readonly ViewFactory _viewFactory;
    private readonly ViewBinder _binder;
    private readonly IUiDispatcher _dispatcher;
    private readonly BrowserNavigationSessionStore _sessions;
    private IServiceScope? _routerScope;
    private IRouter? _router;
    private IRouteOutlet? _outlet;
    private Guid? _workspaceId;
    private bool _disposed;

    public AtomPixCityNavigator(
        IServiceScopeFactory scopeFactory,
        IRoutePresentationCoordinator coordinator,
        IViewLocator locator,
        ViewFactory viewFactory,
        ViewBinder binder,
        IUiDispatcher dispatcher,
        BrowserNavigationSessionStore sessions)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _viewFactory = viewFactory ?? throw new ArgumentNullException(nameof(viewFactory));
        _binder = binder ?? throw new ArgumentNullException(nameof(binder));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
    }

    public bool IsAttached => _router is not null && _outlet is not null;

    public Guid? CurrentWorkspaceId => _workspaceId;

    internal RoutePresentationResult? LastResult { get; private set; }

    public void Attach(IRouteOutlet outlet)
    {
        ArgumentNullException.ThrowIfNull(outlet);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsAttached)
        {
            throw new InvalidOperationException("AtomPix navigation is already attached to an outlet.");
        }

        _routerScope = _scopeFactory.CreateScope();
        _router = _routerScope.ServiceProvider.GetRequiredService<IRouter>();
        _outlet = outlet;
    }

    public async Task<bool> PresentAsync(
        DesktopNavigationRequest request,
        object viewModel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(viewModel);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_router is null || _outlet is null)
        {
            return false;
        }

        var intent = BuildIntent(request);
        return await PresentCoreAsync(intent, viewModel, cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> ResumeBrowserAsync(
        object viewModel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _workspaceId is Guid workspaceId
            ? PresentCoreAsync(
                new NavigationIntent($"browser/resume/{workspaceId:D}", workspaceId, null),
                viewModel,
                cancellationToken)
            : Task.FromResult(false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _routerScope?.Dispose();
        _routerScope = null;
        _router = null;
        _outlet = null;
    }

    private NavigationIntent BuildIntent(DesktopNavigationRequest request)
    {
        if (request.Route == DesktopRoute.Settings)
        {
            return new NavigationIntent("settings", _workspaceId, null);
        }

        if (request.Route == DesktopRoute.Browse && request.Context is null)
        {
            return new NavigationIntent("home", null, null);
        }

        var context = request.Context switch
        {
            BrowserNavigationContext browser => browser,
            BrowserToolNavigationContext tool => tool.Browser,
            _ => null,
        };
        if (context is not null)
        {
            var key = _sessions.Add(context);
            return new NavigationIntent($"browser/open/{key:D}", Guid.NewGuid(), key);
        }

        var workspaceId = _workspaceId ?? Guid.NewGuid();
        return new NavigationIntent($"browser/resume/{workspaceId:D}", workspaceId, null);
    }

    private async Task<bool> PresentCoreAsync(
        NavigationIntent intent,
        object viewModel,
        CancellationToken cancellationToken)
    {
        if (_router is null || _outlet is null)
        {
            intent.Discard(_sessions);
            return false;
        }

        try
        {
            var result = await _coordinator.NavigateAsync(
                _router,
                _outlet,
                token => _router.NavigateByPathAsync(
                    intent.Path,
                    new NavigationOptions
                    {
                        Mode = NavigationMode.Replace,
                        HistoryBehavior = NavigationHistoryBehavior.Record,
                        OutletName = "primary",
                        ForceReload = true,
                    },
                    token),
                new PreparedRoutePlanFactory(
                    viewModel,
                    _locator,
                    _viewFactory,
                    _binder,
                    _dispatcher,
                    intent.SessionKey is Guid sessionKey
                        ? context => ConsumeSession(context, sessionKey)
                        : null),
                cancellationToken).ConfigureAwait(false);
            LastResult = result;
            if (!result.Succeeded)
            {
                return false;
            }

            _workspaceId = intent.WorkspaceId;
            return true;
        }
        finally
        {
            intent.Discard(_sessions);
        }
    }

    private void ConsumeSession(RoutePresentationPlanContext context, Guid expectedKey)
    {
        if (!context.Navigation.Parameters.TryGetValue("sessionKey", out var value)
            || !Guid.TryParse(value, out var actualKey)
            || actualKey != expectedKey
            || !_sessions.TryConsume(expectedKey, out _))
        {
            throw new InvalidOperationException(
                "Browser navigation session is missing, expired, or was already consumed.");
        }
    }

    private sealed record NavigationIntent(string Path, Guid? WorkspaceId, Guid? SessionKey)
    {
        public void Discard(BrowserNavigationSessionStore sessions)
        {
            if (SessionKey is Guid key)
            {
                sessions.Discard(key);
            }
        }
    }
}
