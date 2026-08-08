namespace AtomPix.Desktop.Shell;

using AtomPix.Desktop.Navigation;
using AtomPix.Desktop.Platform;
using AtomPix.Desktop.ViewModels;

public sealed class ShellViewModel : ObservableObject, IDisposable
{
    private readonly DesktopNavigationCoordinator _navigation;
    private readonly HomePageViewModel _home;
    private readonly ImageBrowserViewModel _browser;
    private readonly CompressionEditorViewModel _compress;
    private readonly ConversionEditorViewModel _convert;
    private readonly ResizeEditorViewModel _resize;
    private readonly CropEditorViewModel _crop;
    private readonly BatchTaskViewModel _batch;
    private readonly SettingsPageViewModel _settings;
    private readonly IDesktopDialogService _dialogs;
    private object _currentPage;
    private DesktopRoute _currentRoute = DesktopRoute.Browse;
    private bool _isForegroundTaskActive;
    private bool _isNavigationPending;
    private int _navigationRevision;

    public ShellViewModel(
        DesktopNavigationCoordinator navigation,
        HomePageViewModel home,
        ImageBrowserViewModel browser,
        CompressionEditorViewModel compress,
        ConversionEditorViewModel convert,
        ResizeEditorViewModel resize,
        CropEditorViewModel crop,
        BatchTaskViewModel batch,
        SettingsPageViewModel settings,
        IDesktopDialogService dialogs)
    {
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _home = home ?? throw new ArgumentNullException(nameof(home));
        _browser = browser ?? throw new ArgumentNullException(nameof(browser));
        _compress = compress ?? throw new ArgumentNullException(nameof(compress));
        _convert = convert ?? throw new ArgumentNullException(nameof(convert));
        _resize = resize ?? throw new ArgumentNullException(nameof(resize));
        _crop = crop ?? throw new ArgumentNullException(nameof(crop));
        _batch = batch ?? throw new ArgumentNullException(nameof(batch));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _currentPage = _home;
        NavigateCommand = new RelayCommand<DesktopRoute>(route => _ = NavigateAsync(route), _ => !IsForegroundTaskActive && !_isNavigationPending);
        _navigation.NavigationRequested += HandleNavigationRequested;
        _navigation.NavigationLockChanged += HandleNavigationLockChanged;
        _ = _home.LoadRecentAsync();
        _ = _settings.LoadAsync();
    }

    public string ApplicationTitle => "AtomPix";

    public DesktopRoute CurrentRoute
    {
        get => _currentRoute;
        private set => SetProperty(ref _currentRoute, value);
    }

    public object CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

    public int NavigationRevision
    {
        get => _navigationRevision;
        private set => SetProperty(ref _navigationRevision, value);
    }

    public bool IsForegroundTaskActive
    {
        get => _isForegroundTaskActive;
        private set
        {
            if (SetProperty(ref _isForegroundTaskActive, value))
            {
                NavigateCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public RelayCommand<DesktopRoute> NavigateCommand { get; }

    public void SetForegroundTaskActive(bool value) => _navigation.SetNavigationLocked(value);

    public void RefreshResultAvailability()
    {
        if (CurrentPage is IResultAvailabilityAware resultPage)
        {
            resultPage.RefreshResultAvailability();
        }
    }

    public void Dispose()
    {
        _navigation.NavigationRequested -= HandleNavigationRequested;
        _navigation.NavigationLockChanged -= HandleNavigationLockChanged;
        _browser.Dispose();
        _compress.Dispose();
        _convert.Dispose();
        _resize.Dispose();
        _crop.Dispose();
        _batch.Dispose();
        _settings.Dispose();
    }

    public async Task<bool> TryCloseAsync(CancellationToken cancellationToken = default)
    {
        if (IsForegroundTaskActive)
        {
            var cancel = await _dialogs.ConfirmAsync(
                "取消任务并退出？",
                "AtomPix 会请求取消当前任务，保留已经完成并成功提交的输出，然后等待任务进入终态再关闭。",
                "取消任务并退出",
                "继续处理",
                cancellationToken);
            if (!cancel) return false;

            if (CurrentPage is IDesktopForegroundTask task) task.RequestCancellation();
            await WaitForForegroundTaskAsync(cancellationToken);
        }

        return await _settings.TryLeaveAsync(cancellationToken);
    }

    private async Task NavigateAsync(DesktopRoute route)
    {
        if (route == CurrentRoute || _isNavigationPending) return;

        _isNavigationPending = true;
        NavigateCommand.NotifyCanExecuteChanged();
        try
        {
            if (CurrentRoute == DesktopRoute.Settings && !await _settings.TryLeaveAsync()) return;
            _navigation.Navigate(new DesktopNavigationRequest(route));
        }
        finally
        {
            _isNavigationPending = false;
            NavigateCommand.NotifyCanExecuteChanged();
            NavigationRevision++;
        }
    }

    private Task WaitForForegroundTaskAsync(CancellationToken cancellationToken)
    {
        if (!IsForegroundTaskActive) return Task.CompletedTask;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? sender, bool isLocked)
        {
            if (!isLocked) completion.TrySetResult();
        }

        _navigation.NavigationLockChanged += Handler;
        return AwaitAndDetachAsync();

        async Task AwaitAndDetachAsync()
        {
            try { await completion.Task.WaitAsync(cancellationToken); }
            finally { _navigation.NavigationLockChanged -= Handler; }
        }
    }

    private void HandleNavigationRequested(object? sender, DesktopNavigationRequest request)
    {
        CurrentRoute = request.Route;
        switch (request.Route)
        {
            case DesktopRoute.Browse when request.Context is BrowserNavigationContext browserContext:
                CurrentPage = _browser;
                _ = _browser.LoadAsync(browserContext);
                break;

            case DesktopRoute.Browse:
                CurrentPage = _home;
                _ = _home.LoadRecentAsync(force: true);
                break;

            case DesktopRoute.Compress:
                if (request.Context is SingleImageNavigationContext compressContext)
                {
                    _ = _compress.LoadAsync(compressContext);
                }
                else
                {
                    _compress.Clear();
                }
                CurrentPage = _compress;
                break;

            case DesktopRoute.Convert:
                if (request.Context is SingleImageNavigationContext convertContext)
                {
                    _ = _convert.LoadAsync(convertContext);
                }
                else
                {
                    _convert.Clear();
                }
                CurrentPage = _convert;
                break;

            case DesktopRoute.Crop:
                if (request.Context is SingleImageNavigationContext cropContext) _ = _crop.LoadAsync(cropContext);
                else _crop.Clear();
                CurrentPage = _crop;
                break;

            case DesktopRoute.Resize:
                if (request.Context is SingleImageNavigationContext resizeContext)
                {
                    _ = _resize.LoadAsync(resizeContext);
                }
                else
                {
                    _resize.Clear();
                }

                CurrentPage = _resize;
                break;

            case DesktopRoute.Batch:
                _ = _batch.LoadAsync();
                CurrentPage = _batch;
                break;

            case DesktopRoute.Settings:
                CurrentPage = _settings;
                _ = _settings.LoadAsync();
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(request.Route), request.Route, null);
        }

        RefreshResultAvailability();
        NavigationRevision++;
    }

    private void HandleNavigationLockChanged(object? sender, bool isLocked) =>
        IsForegroundTaskActive = isLocked;
}
