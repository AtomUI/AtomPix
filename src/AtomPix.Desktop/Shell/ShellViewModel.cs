namespace AtomPix.Desktop.Shell;

using System.Collections.Specialized;
using System.ComponentModel;
using AtomPix.Core.Jobs;
using AtomPix.Core.ValueObjects;
using AtomPix.Desktop.Navigation;
using AtomPix.Desktop.Platform;
using AtomPix.Desktop.ViewModels;
using AtomPix.Workflows.Images;

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
    private readonly IDesktopFeedbackService _feedback;
    private object _currentPage;
    private object _pageBeforeSettings;
    private object? _drawerContent;
    private DesktopRoute _currentRoute = DesktopRoute.Browse;
    private DesktopRoute _routeBeforeSettings = DesktopRoute.Browse;
    private string _drawerTitle = string.Empty;
    private bool _isToolDrawerOpen;
    private bool _isBatchResultOpen;
    private bool _batchResultPublished;
    private int _batchResultNotificationGeneration;
    private bool _isForegroundTaskActive;
    private bool _isNavigationPending;
    private int _navigationRevision;
    private ToolDrawerSessionViewModel? _toolSession;

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
        IDesktopDialogService dialogs,
        IDesktopFeedbackService feedback)
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
        _feedback = feedback ?? throw new ArgumentNullException(nameof(feedback));
        _currentPage = _home;
        _pageBeforeSettings = _home;

        NavigateCommand = new RelayCommand<DesktopRoute>(route => _ = NavigateAsync(route), CanNavigateTo);
        CloseDrawerCommand = new RelayCommand<object?>(_ => CloseToolDrawer(), _ => !IsForegroundTaskActive);
        CloseSettingsCommand = new RelayCommand<object?>(_ => _ = CloseSettingsPageAsync(), _ => IsSettingsOpen && !IsForegroundTaskActive);

        _navigation.NavigationRequested += HandleNavigationRequested;
        _navigation.NavigationLockChanged += HandleNavigationLockChanged;
        _batch.Items.CollectionChanged += HandleBatchItemsChanged;
        _batch.PropertyChanged += HandleBatchPropertyChanged;
        _batch.RecoveryDraftCreated += HandleBatchRecoveryDraftCreated;
        _browser.PropertyChanged += HandleBrowserPropertyChanged;
        _settings.CloseRequested += HandleSettingsCloseRequested;
        SubscribeOperationFeedback(_compress.ResultFeedback);
        SubscribeOperationFeedback(_convert.ResultFeedback);
        SubscribeOperationFeedback(_resize.ResultFeedback);
        SubscribeOperationFeedback(_crop.ResultFeedback);
    }

    public string ApplicationTitle => "AtomPix";
    public HomePageViewModel Home => _home;
    public ImageBrowserViewModel Browser => _browser;
    public SettingsPageViewModel Settings => _settings;
    public CropEditorViewModel Crop => _crop;
    public BatchTaskViewModel BatchResultDetails => _batch;

    public DesktopRoute CurrentRoute
    {
        get => _currentRoute;
        private set
        {
            if (!SetProperty(ref _currentRoute, value)) return;
            OnPropertyChanged(nameof(IsCompressActive));
            OnPropertyChanged(nameof(IsConvertActive));
            OnPropertyChanged(nameof(IsCropActive));
            OnPropertyChanged(nameof(IsResizeActive));
            OnPropertyChanged(nameof(IsSettingsActive));
            OnPropertyChanged(nameof(IsSettingsOpen));
            OnPropertyChanged(nameof(IsPrimaryNavigationVisible));
            OnPropertyChanged(nameof(IsToolPanelVisible));
            NavigateCommand.NotifyCanExecuteChanged();
        }
    }

    public object CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (!SetProperty(ref _currentPage, value)) return;
            OnPropertyChanged(nameof(IsHomeVisible));
            OnPropertyChanged(nameof(IsBrowserVisible));
            OnPropertyChanged(nameof(IsSettingsOpen));
            OnPropertyChanged(nameof(IsSettingsActive));
            OnPropertyChanged(nameof(IsPrimaryNavigationVisible));
            OnPropertyChanged(nameof(IsToolPanelVisible));
        }
    }

    public object? DrawerContent
    {
        get => _drawerContent;
        private set => SetProperty(ref _drawerContent, value);
    }

    public string DrawerTitle
    {
        get => _drawerTitle;
        private set => SetProperty(ref _drawerTitle, value);
    }


    public bool IsToolDrawerOpen
    {
        get => _isToolDrawerOpen;
        set
        {
            if (!SetProperty(ref _isToolDrawerOpen, value)) return;
            OnPropertyChanged(nameof(IsCompressActive));
            OnPropertyChanged(nameof(IsConvertActive));
            OnPropertyChanged(nameof(IsCropActive));
            OnPropertyChanged(nameof(IsResizeActive));
            OnPropertyChanged(nameof(IsToolPanelVisible));
            if (value) return;
            DrawerContent = null;
            DrawerTitle = string.Empty;
            _toolSession?.Dispose();
            _toolSession = null;
            _browser.SetCropMode(false);
            if (CurrentRoute != DesktopRoute.Settings) CurrentRoute = DesktopRoute.Browse;
        }
    }

    public bool IsBatchResultOpen
    {
        get => _isBatchResultOpen;
        set => SetProperty(ref _isBatchResultOpen, value);
    }

    public bool IsHomeVisible => ReferenceEquals(CurrentPage, _home);
    public bool IsBrowserVisible => ReferenceEquals(CurrentPage, _browser);
    public bool IsCompressActive => IsToolDrawerOpen && CurrentRoute == DesktopRoute.Compress;
    public bool IsConvertActive => IsToolDrawerOpen && CurrentRoute == DesktopRoute.Convert;
    public bool IsCropActive => IsToolDrawerOpen && CurrentRoute == DesktopRoute.Crop;
    public bool IsResizeActive => IsToolDrawerOpen && CurrentRoute == DesktopRoute.Resize;
    public bool IsSettingsOpen => CurrentRoute == DesktopRoute.Settings && ReferenceEquals(CurrentPage, _settings);
    public bool IsSettingsActive => IsSettingsOpen;
    public bool IsPrimaryNavigationVisible => !IsSettingsOpen;
    public bool IsToolPanelVisible => IsToolDrawerOpen && !IsSettingsOpen;

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
            if (!SetProperty(ref _isForegroundTaskActive, value)) return;
            NavigateCommand.NotifyCanExecuteChanged();
            CloseDrawerCommand.NotifyCanExecuteChanged();
            CloseSettingsCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanNavigate => !IsForegroundTaskActive && !_isNavigationPending;

    private bool CanNavigateTo(DesktopRoute route)
    {
        if (!CanNavigate) return false;
        if (!ReferenceEquals(CurrentPage, _browser)) return true;
        return route switch
        {
            DesktopRoute.Compress => _browser.CanCompress,
            DesktopRoute.Convert => _browser.CanConvert,
            DesktopRoute.Resize => _browser.CanResize,
            DesktopRoute.Crop => _browser.CanCrop,
            _ => true
        };
    }

    public RelayCommand<DesktopRoute> NavigateCommand { get; }
    public RelayCommand<object?> CloseDrawerCommand { get; }
    public RelayCommand<object?> CloseSettingsCommand { get; }

    public void SetForegroundTaskActive(bool value) => _navigation.SetNavigationLocked(value);

    public void RefreshResultAvailability()
    {
        foreach (var page in new object[] { _compress, _convert, _resize, _crop, _batch })
        {
            if (page is IResultAvailabilityAware aware) aware.RefreshResultAvailability();
        }
    }

    public void Dispose()
    {
        _navigation.NavigationRequested -= HandleNavigationRequested;
        _navigation.NavigationLockChanged -= HandleNavigationLockChanged;
        _batch.Items.CollectionChanged -= HandleBatchItemsChanged;
        _batch.PropertyChanged -= HandleBatchPropertyChanged;
        _batch.RecoveryDraftCreated -= HandleBatchRecoveryDraftCreated;
        _browser.PropertyChanged -= HandleBrowserPropertyChanged;
        _settings.CloseRequested -= HandleSettingsCloseRequested;
        UnsubscribeOperationFeedback(_compress.ResultFeedback);
        UnsubscribeOperationFeedback(_convert.ResultFeedback);
        UnsubscribeOperationFeedback(_resize.ResultFeedback);
        UnsubscribeOperationFeedback(_crop.ResultFeedback);
        _toolSession?.Dispose();
        DetachBatchItems(_batch.Items);
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

            if (DrawerContent is IDesktopForegroundTask task) task.RequestCancellation();
            else _batch.RequestCancellation();
            await WaitForForegroundTaskAsync(cancellationToken);
        }

        return await _settings.TryLeaveAsync(cancellationToken);
    }

    private async Task NavigateAsync(DesktopRoute route)
    {
        if (_isNavigationPending) return;
        _isNavigationPending = true;
        NavigateCommand.NotifyCanExecuteChanged();
        try
        {
            if (route == DesktopRoute.Settings && IsSettingsOpen) return;
            if (route == CurrentRoute && IsToolDrawerOpen)
            {
                CloseToolDrawer();
                return;
            }

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

    private void HandleNavigationRequested(object? sender, DesktopNavigationRequest request) =>
        _ = HandleNavigationRequestedAsync(request);

    private async Task HandleNavigationRequestedAsync(DesktopNavigationRequest request)
    {
        if (IsSettingsOpen && request.Route != DesktopRoute.Settings
            && !await _settings.TryLeaveAsync())
        {
            return;
        }

        switch (request.Route)
        {
            case DesktopRoute.Browse when request.Context is BrowserNavigationContext browserContext:
                CloseToolDrawer();
                CurrentRoute = DesktopRoute.Browse;
                CurrentPage = _browser;
                await _browser.LoadAsync(browserContext);
                break;

            case DesktopRoute.Browse:
                CloseToolDrawer();
                _browser.EndSession();
                CurrentRoute = DesktopRoute.Browse;
                CurrentPage = _home;
                break;

            case DesktopRoute.Settings:
                _routeBeforeSettings = CurrentRoute;
                _pageBeforeSettings = CurrentPage;
                await _settings.LoadAsync();
                CurrentRoute = DesktopRoute.Settings;
                CurrentPage = _settings;
                break;

            case DesktopRoute.Batch:
                _feedback.ShowMessage("批量处理请从压缩、转换或调整尺寸面板直接开始。", DesktopFeedbackSeverity.Information);
                break;

            case DesktopRoute.Compress or DesktopRoute.Convert or DesktopRoute.Resize or DesktopRoute.Crop:
                if (request.Context is BrowserToolNavigationContext browserToolContext)
                {
                    CloseToolDrawer();
                    CurrentRoute = DesktopRoute.Browse;
                    CurrentPage = _browser;
                    await _browser.LoadAsync(browserToolContext.Browser);
                    if (_browser.TryCreateCurrentContext(out var loadedContext) && CanNavigateTo(request.Route))
                    {
                        await OpenToolAsync(request.Route, loadedContext);
                    }
                }
                else
                {
                    await OpenToolAsync(request.Route, request.Context as SingleImageNavigationContext);
                }
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(request.Route), request.Route, null);
        }

        RefreshResultAvailability();
        NavigationRevision++;
    }

    private void HandleSettingsCloseRequested(object? sender, EventArgs args) =>
        _ = CloseSettingsPageAsync();

    private async Task CloseSettingsPageAsync()
    {
        if (!IsSettingsOpen || IsForegroundTaskActive) return;
        if (!await _settings.TryLeaveAsync()) return;

        CurrentPage = _pageBeforeSettings;
        CurrentRoute = _routeBeforeSettings;
        RefreshResultAvailability();
        NavigationRevision++;
    }

    private async Task OpenToolAsync(DesktopRoute route, SingleImageNavigationContext? context)
    {
        if (context is null && !_browser.TryCreateCurrentContext(out context))
        {
            TriggerHomePicker(route);
            return;
        }

        if (!ReferenceEquals(CurrentPage, _browser))
        {
            CurrentPage = _browser;
            var item = new BrowserImageCandidate(context!.InputPath, Path.GetFileName(context.InputPath.Value));
            await _browser.LoadAsync(new BrowserNavigationContext(null, [item], context.InputPath, context.Probe));
        }

        object content = route switch
        {
            DesktopRoute.Compress => _compress,
            DesktopRoute.Convert => _convert,
            DesktopRoute.Resize => _resize,
            DesktopRoute.Crop => _crop,
            _ => throw new ArgumentOutOfRangeException(nameof(route))
        };

        var loadTask = content switch
        {
            CompressionEditorViewModel vm => vm.LoadAsync(context!),
            ConversionEditorViewModel vm => vm.LoadAsync(context!),
            ResizeEditorViewModel vm => vm.LoadAsync(context!),
            CropEditorViewModel vm => vm.LoadAsync(context!),
            _ => Task.CompletedTask
        };

        // Load the editor context before switching ImageGallery into Crop's
        // ResourceOnly mode. CropEditorViewModel initializes its input path and
        // selection synchronously before its first await; the view can therefore
        // match and acquire the already displayed browser image immediately.
        _browser.SetCropMode(route == DesktopRoute.Crop, route == DesktopRoute.Crop ? _crop : null);

        _browser.ResetBatchStatuses();
        _toolSession?.Dispose();
        _toolSession = null;
        if (route != DesktopRoute.Crop)
        {
            // Keep a stable reference to the actual editor. The local `content` variable
            // is replaced with the session below; capturing it directly would make the
            // deferred batch callback receive ToolDrawerSessionViewModel instead of the
            // compression/conversion/resize editor.
            var editorContent = content;
            _toolSession = new ToolDrawerSessionViewModel(
                editorContent,
                _batch,
                _browser.Items.Count,
                cancellationToken => PrepareBatchForToolAsync(route, editorContent, cancellationToken),
                () => SynchronizeSingleDraftFromBatch(editorContent),
                _navigation);
            content = _toolSession;
        }
        OpenDrawer(route, BuildDrawerTitle(route), content);
        await loadTask;
    }

    private async Task<bool> PrepareBatchForToolAsync(DesktopRoute route, object editor, CancellationToken cancellationToken)
    {
        var kind = route switch
        {
            DesktopRoute.Compress => BatchTaskKind.Compress,
            DesktopRoute.Convert => BatchTaskKind.Convert,
            DesktopRoute.Resize => BatchTaskKind.Resize,
            _ => throw new ArgumentOutOfRangeException(nameof(route))
        };
        if (!TryCaptureBatchDraft(editor, _batch, out var applyDraft))
        {
            _feedback.ShowMessage(ResolveBatchDraftError(editor), DesktopFeedbackSeverity.Warning);
            return false;
        }

        await _batch.PrepareAsync(kind, _browser.Items.Select(item => item.Path).ToArray(), cancellationToken);
        applyDraft!();
        if (_batch.DraftError is null && _batch.CanAttemptStart) return true;

        _feedback.ShowMessage(
            _batch.DraftError ?? _batch.ErrorMessage ?? ResolveBatchStartBlockReason(_batch),
            DesktopFeedbackSeverity.Warning);
        return false;
    }

    private static string ResolveBatchStartBlockReason(BatchTaskViewModel batch)
    {
        if (!batch.HasInputs) return "当前图片集合为空，无法开始批量处理。";
        if (batch.IsAppending) return "正在追加或读取图片，请稍后再试。";
        if (batch.IsProcessing) return "已有批量任务正在运行。";
        if (batch.HasResult) return "请先结束当前批量结果上下文，再开始新任务。";
        return "批量任务尚未准备完成，请重试。";
    }

    private static string ResolveBatchDraftError(object editor) => editor switch
    {
        CompressionEditorViewModel vm => vm.DraftError ?? vm.Output.ValidationError ?? "当前压缩配置无效。",
        ConversionEditorViewModel vm => vm.DraftError ?? vm.Output.ValidationError ?? "当前转换配置无效。",
        ResizeEditorViewModel vm => vm.DraftError ?? vm.Output.ValidationError ?? "当前尺寸配置无效。",
        _ => "当前批量处理配置无效。"
    };

    internal static bool TryCaptureBatchDraft(object editor, BatchTaskViewModel batch, out Action? applyDraft)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(batch);
        applyDraft = null;
        if (editor is not IToolEditorActions actions || !actions.StartActionCommand.CanExecute(null)) return false;

        // Capture the complete visible draft before the first await. A failed draft
        // must reject submission; it must never fall through to BatchTaskViewModel's
        // persisted defaults.
        switch (editor)
        {
            case CompressionEditorViewModel vm when vm.Output.TryBuild(out var output, out _) && output is not null:
                var compressionMode = vm.SelectedMode.Value;
                var compressionQuality = vm.CustomQuality;
                var compressionRemoveMetadata = vm.RemoveMetadata;
                applyDraft = () => batch.ApplyCompressionDraft(
                    compressionMode,
                    compressionQuality,
                    compressionRemoveMetadata,
                    output);
                break;
            case ConversionEditorViewModel vm when vm.Output.TryBuild(out var output, out _) && output is not null:
                var conversionFormat = vm.SelectedFormat.Value;
                var conversionQuality = vm.Quality;
                var conversionBackground = vm.BackgroundHex;
                var conversionRemoveMetadata = vm.RemoveMetadata;
                applyDraft = () => batch.ApplyConversionDraft(
                    conversionFormat,
                    conversionQuality,
                    conversionBackground,
                    conversionRemoveMetadata,
                    output);
                break;
            case ResizeEditorViewModel vm when vm.Output.TryBuild(out var output, out _) && output is not null:
                var resizeMode = vm.SelectedMode.Value;
                var pixelWidth = vm.PixelWidth;
                var pixelHeight = vm.PixelHeight;
                var pixelAnchor = vm.PixelAnchor;
                var maintainAspectRatio = vm.MaintainAspectRatio;
                var preventUpscaling = vm.PreventUpscaling;
                var percentage = vm.Percentage;
                applyDraft = () => batch.ApplyResizeDraft(
                    resizeMode,
                    pixelWidth,
                    pixelHeight,
                    pixelAnchor,
                    maintainAspectRatio,
                    preventUpscaling,
                    percentage,
                    vm.EncodingPolicy,
                    output);
                break;
            default:
                return false;
        }
        return true;
    }

    private void SynchronizeSingleDraftFromBatch(object editor)
    {
        switch (editor)
        {
            case CompressionEditorViewModel vm:
                vm.SelectedMode = vm.Modes.First(option => option.Value == _batch.SelectedCompressionMode.Value);
                vm.CustomQuality = _batch.CustomQuality;
                vm.RemoveMetadata = _batch.RemoveMetadata;
                if (_batch.Output.TryBuild(out var compressionOutput, out _) && compressionOutput is not null) vm.Output.Apply(compressionOutput);
                break;
            case ConversionEditorViewModel vm:
                vm.SelectedFormat = vm.Formats.First(option => option.Value == _batch.SelectedFormat.Value);
                vm.Quality = _batch.ConversionQuality;
                vm.BackgroundHex = _batch.BackgroundHex;
                vm.RemoveMetadata = _batch.RemoveMetadata;
                if (_batch.Output.TryBuild(out var conversionOutput, out _) && conversionOutput is not null) vm.Output.Apply(conversionOutput);
                break;
            case ResizeEditorViewModel vm:
                vm.ApplyResizeDraft(
                    _batch.SelectedResizeMode.Value,
                    _batch.PixelWidth,
                    _batch.PixelHeight,
                    _batch.PixelAnchor,
                    _batch.MaintainAspectRatio,
                    _batch.PreventUpscaling,
                    _batch.Percentage);
                if (_batch.Output.TryBuild(out var resizeOutput, out _) && resizeOutput is not null) vm.Output.Apply(resizeOutput);
                break;
        }
    }

    private void TriggerHomePicker(DesktopRoute route)
    {
        var command = route switch
        {
            DesktopRoute.Compress => _home.OpenForCompressCommand,
            DesktopRoute.Convert => _home.OpenForConvertCommand,
            DesktopRoute.Resize => _home.OpenForResizeCommand,
            DesktopRoute.Crop => _home.OpenForCropCommand,
            _ => null
        };
        command?.Execute(null);
    }

    private void OpenDrawer(DesktopRoute route, string title, object content)
    {
        CurrentRoute = route;
        DrawerTitle = title;
        DrawerContent = content;
        IsToolDrawerOpen = true;
    }

    private void CloseToolDrawer()
    {
        if (IsForegroundTaskActive) return;
        IsToolDrawerOpen = false;
    }

    private static string GetToolTitle(DesktopRoute route, bool batch) => route switch
    {
        DesktopRoute.Compress => batch ? "批量压缩" : "压缩图片",
        DesktopRoute.Convert => batch ? "批量转换" : "转换格式",
        DesktopRoute.Resize => batch ? "批量调整尺寸" : "调整尺寸",
        DesktopRoute.Crop => "裁剪图片",
        _ => string.Empty
    };

    private static string BuildDrawerTitle(DesktopRoute route) => GetToolTitle(route, false);

    private void HandleNavigationLockChanged(object? sender, bool isLocked)
    {
        IsForegroundTaskActive = isLocked;
        _browser.SetInteractionLocked(isLocked);
    }

    private void HandleBrowserPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        NavigateCommand.NotifyCanExecuteChanged();
        if (args.PropertyName == nameof(ImageBrowserViewModel.Items))
        {
            _toolSession?.UpdateItemCount(_browser.Items.Count);
            if (IsToolDrawerOpen && CurrentRoute is DesktopRoute.Compress or DesktopRoute.Convert or DesktopRoute.Resize or DesktopRoute.Crop)
            {
                DrawerTitle = BuildDrawerTitle(CurrentRoute);
            }
        }
        if (IsToolDrawerOpen
            && _browser.State == DesktopContentState.Ready
            && args.PropertyName is nameof(ImageBrowserViewModel.State) or null or "")
        {
            _ = SynchronizeToolInputAsync();
        }
    }

    private async Task SynchronizeToolInputAsync()
    {
        if (!IsToolDrawerOpen || !_browser.TryCreateCurrentContext(out var context) || context is null) return;

        switch (CurrentRoute)
        {
            case DesktopRoute.Compress when !PathsEqual(_compress.InputPath, context.InputPath.Value):
                await _compress.SynchronizeInputAsync(context);
                break;
            case DesktopRoute.Convert when !PathsEqual(_convert.InputPath, context.InputPath.Value):
                await _convert.SynchronizeInputAsync(context);
                break;
            case DesktopRoute.Resize when !PathsEqual(_resize.InputPath, context.InputPath.Value):
                await _resize.SynchronizeInputAsync(context);
                break;
            case DesktopRoute.Crop when !PathsEqual(_crop.InputPath, context.InputPath.Value):
                await _crop.SynchronizeInputAsync(context);
                break;
        }
    }

    private static bool PathsEqual(string left, string right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private void HandleBatchItemsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.OldItems is not null) DetachBatchItems(args.OldItems.Cast<BatchItemViewModel>());
        if (args.NewItems is not null) AttachBatchItems(args.NewItems.Cast<BatchItemViewModel>());
        SynchronizeAllBatchStatuses();
    }

    private void HandleBatchPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(BatchTaskViewModel.IsProcessing) or nameof(BatchTaskViewModel.HasResult) or null or "")
        {
            SynchronizeAllBatchStatuses();
            PublishBatchResultIfNeeded();
        }
    }

    private void HandleBatchRecoveryDraftCreated(object? sender, IReadOnlyList<LocalPath> paths)
    {
        IsBatchResultOpen = false;
        _toolSession?.SynchronizeDraftFromBatch();
        _ = ReplaceBrowserCollectionForRecoveryAsync(paths);
    }

    private void PublishBatchResultIfNeeded()
    {
        if (!_batch.HasResult)
        {
            _batchResultPublished = false;
            return;
        }
        if (_batchResultPublished) return;
        _batchResultPublished = true;
        var notificationGeneration = ++_batchResultNotificationGeneration;

        var isCleanSuccess = !_batch.HasFailedItems && !_batch.HasSkippedItems && !_batch.HasUnfinishedItems;
        var severity = isCleanSuccess
            ? DesktopFeedbackSeverity.Success
            : _batch.HasFailedItems
                ? DesktopFeedbackSeverity.Error
                : DesktopFeedbackSeverity.Warning;
        var operation = _batch.SelectedTask.Value switch
        {
            BatchTaskKind.Compress => "批量压缩",
            BatchTaskKind.Convert => "批量转换",
            BatchTaskKind.Resize => "批量调整尺寸",
            _ => "批量处理"
        };
        var content = $"{_batch.ResultSummary}{Environment.NewLine}{_batch.ResultSizeChange}{Environment.NewLine}点击查看处理详情";
        _feedback.ShowNotification(new DesktopNotificationRequest(
            $"{operation}：{_batch.ResultTitle}",
            content,
            severity,
            isCleanSuccess ? TimeSpan.FromSeconds(6) : TimeSpan.Zero,
            () =>
            {
                if (notificationGeneration == _batchResultNotificationGeneration && _batch.HasResult)
                {
                    IsBatchResultOpen = true;
                }
            }));
    }

    private void SubscribeOperationFeedback(OperationResultFeedback feedback) =>
        feedback.PropertyChanged += HandleOperationFeedbackPropertyChanged;

    private void UnsubscribeOperationFeedback(OperationResultFeedback feedback) =>
        feedback.PropertyChanged -= HandleOperationFeedbackPropertyChanged;

    private void HandleOperationFeedbackPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(OperationResultFeedback.Generation)
            || sender is not OperationResultFeedback { IsVisible: true } feedback)
        {
            return;
        }

        _feedback.ShowMessage(
            feedback.Message,
            feedback.Severity switch
            {
                OperationFeedbackSeverity.Success => DesktopFeedbackSeverity.Success,
                OperationFeedbackSeverity.Warning => DesktopFeedbackSeverity.Warning,
                _ => DesktopFeedbackSeverity.Information
            });
        feedback.Dismiss();
    }

    private async Task ReplaceBrowserCollectionForRecoveryAsync(IReadOnlyList<LocalPath> paths)
    {
        if (paths.Count == 0) return;
        var candidates = paths
            .Select(path => new BrowserImageCandidate(path, Path.GetFileName(path.Value)))
            .ToArray();
        await _browser.LoadAsync(new BrowserNavigationContext(null, candidates, paths[0], null));
        _toolSession?.UpdateItemCount(_browser.Items.Count);
    }

    private void AttachBatchItems(IEnumerable<BatchItemViewModel> items)
    {
        foreach (var item in items) item.PropertyChanged += HandleBatchItemPropertyChanged;
    }

    private void DetachBatchItems(IEnumerable<BatchItemViewModel> items)
    {
        foreach (var item in items) item.PropertyChanged -= HandleBatchItemPropertyChanged;
    }

    private void HandleBatchItemPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (sender is BatchItemViewModel item
            && args.PropertyName is nameof(BatchItemViewModel.StatusText) or nameof(BatchItemViewModel.TerminalStatus) or null or "")
        {
            SynchronizeBatchStatus(item);
        }
    }

    private void SynchronizeAllBatchStatuses()
    {
        _browser.ResetBatchStatuses();
        if (!_batch.IsProcessing && !_batch.HasResult) return;
        foreach (var item in _batch.Items) SynchronizeBatchStatus(item);
    }

    private void SynchronizeBatchStatus(BatchItemViewModel item)
    {
        var status = item.IsRunning
            ? BrowserTaskStatus.Running
            : item.TerminalStatus switch
            {
                ImageJobStatus.Succeeded => BrowserTaskStatus.Succeeded,
                ImageJobStatus.Failed => BrowserTaskStatus.Failed,
                ImageJobStatus.Skipped => BrowserTaskStatus.Skipped,
                ImageJobStatus.Canceled => BrowserTaskStatus.Canceled,
                _ when item.StatusText == "不可用" => BrowserTaskStatus.Failed,
                _ => BrowserTaskStatus.Pending
            };
        _browser.SetBatchStatus(item.Path, status);
    }
}
