namespace AtomPix.Desktop.ViewModels;

using AtomPix.Core.ValueObjects;
using AtomPix.Core.Errors;
using AtomPix.Desktop.Navigation;
using AtomPix.Desktop.Platform;
using AtomPix.Core.Conversion;
using AtomPix.Imaging.Abstractions.Formats;
using AtomPix.Imaging.Abstractions.Processing;
using AtomPix.Workflows.Images;
using AtomUI.Labs.Controls.ImageGallery;

public enum BrowserItemAvailability
{
    Pending,
    Ready,
    Unavailable
}

public enum BrowserTaskStatus
{
    None,
    Pending,
    Running,
    Succeeded,
    Failed,
    Skipped,
    Canceled
}

public sealed class BrowserItemViewModel : ObservableObject
{
    private ImageProbeResult? _probe;
    private BrowserItemAvailability _availability;
    private BrowserTaskStatus _taskStatus;

    public BrowserItemViewModel(
        LocalPath path,
        string displayName,
        Action<BrowserItemViewModel> removeUnavailable)
    {
        Path = path;
        DisplayName = displayName;
        ArgumentNullException.ThrowIfNull(removeUnavailable);
        RemoveCommand = new RelayCommand<object?>(_ => removeUnavailable(this), _ => IsUnavailable);
        GalleryItem = new ImageGalleryItemAdapter(this);
    }

    public LocalPath Path { get; }

    public ImageGalleryItemAdapter GalleryItem { get; }

    public string DisplayName { get; }

    public BrowserItemAvailability Availability
    {
        get => _availability;
        internal set
        {
            if (SetProperty(ref _availability, value))
            {
                OnPropertyChanged(nameof(IsUnavailable));
                RemoveCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsUnavailable => Availability == BrowserItemAvailability.Unavailable;
    public ImageProbeResult? Probe => _probe;

    public BrowserTaskStatus TaskStatus
    {
        get => _taskStatus;
        internal set
        {
            if (!SetProperty(ref _taskStatus, value)) return;
            OnPropertyChanged(nameof(HasTaskStatus));
            OnPropertyChanged(nameof(IsTaskPending));
            OnPropertyChanged(nameof(IsTaskRunning));
            OnPropertyChanged(nameof(IsTaskSucceeded));
            OnPropertyChanged(nameof(IsTaskFailed));
            OnPropertyChanged(nameof(IsTaskSkipped));
            OnPropertyChanged(nameof(IsTaskCanceled));
            OnPropertyChanged(nameof(TaskStatusText));
            OnPropertyChanged(nameof(ThumbnailAutomationName));
        }
    }

    public bool HasTaskStatus => TaskStatus != BrowserTaskStatus.None;
    public bool IsTaskPending => TaskStatus == BrowserTaskStatus.Pending;
    public bool IsTaskRunning => TaskStatus == BrowserTaskStatus.Running;
    public bool IsTaskSucceeded => TaskStatus == BrowserTaskStatus.Succeeded;
    public bool IsTaskFailed => TaskStatus == BrowserTaskStatus.Failed;
    public bool IsTaskSkipped => TaskStatus == BrowserTaskStatus.Skipped;
    public bool IsTaskCanceled => TaskStatus == BrowserTaskStatus.Canceled;
    public string TaskStatusText => TaskStatus switch
    {
        BrowserTaskStatus.Pending => "等待处理",
        BrowserTaskStatus.Running => "正在处理",
        BrowserTaskStatus.Succeeded => "处理成功",
        BrowserTaskStatus.Failed => "处理失败",
        BrowserTaskStatus.Skipped => "已跳过",
        BrowserTaskStatus.Canceled => "已取消",
        _ => string.Empty
    };
    public string ThumbnailAutomationName => HasTaskStatus
        ? $"{DisplayName}，{TaskStatusText}"
        : DisplayName;

    public RelayCommand<object?> RemoveCommand { get; }

    public void SetProbe(ImageProbeResult probe)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        OnPropertyChanged(nameof(Probe));
    }
}

public sealed class ImageBrowserViewModel : ObservableObject, IDisposable
{
    private readonly OpenImageWorkflow _openImage;
    private readonly ImageProcessorCapabilities _capabilities;
    private readonly IDesktopNavigator _navigator;
    private readonly IDesktopLauncherService _launcher;
    private readonly IDesktopPickerService? _picker;
    private readonly BoundedLruCache<string, ImageProbeResult> _probeCache;
    private readonly object _cacheSync = new();
    private CancellationTokenSource? _loadCancellation;
    private int _generation;
    private DesktopContentState _state = DesktopContentState.Empty;
    private IReadOnlyList<BrowserItemViewModel> _items = Array.Empty<BrowserItemViewModel>();
    private IReadOnlyList<ImageGalleryItemAdapter> _galleryItems = Array.Empty<ImageGalleryItemAdapter>();
    private BrowserItemViewModel? _currentItem;
    private ImageProbeResult? _currentProbe;
    private string? _errorMessage;
    private LocalPath? _directoryPath;
    private int _activeBatchIndex = -1;
    private bool _isCropMode;
    private CropEditorViewModel? _cropEditor;
    private bool _isInteractionLocked;

    public ImageBrowserViewModel(
        OpenImageWorkflow openImage,
        IImageProcessor imageProcessor,
        IDesktopNavigator navigator,
        IDesktopLauncherService launcher,
        IDesktopClipboardService clipboard,
        IDesktopPickerService? picker = null)
    {
        _openImage = openImage ?? throw new ArgumentNullException(nameof(openImage));
        _capabilities = (imageProcessor ?? throw new ArgumentNullException(nameof(imageProcessor))).Capabilities;
        var resources = _capabilities.Resources;
        GalleryLoadLimits = new ImageGalleryLoadLimits(
            resources.MaxInputFileSizeBytes,
            resources.MaxInputPixelCount,
            Math.Max(resources.MaxInputWidth, resources.MaxInputHeight),
            resources.MaxInputPixelCount > long.MaxValue / 4
                ? long.MaxValue
                : resources.MaxInputPixelCount * 4);
        _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _picker = picker;
        var pathComparer = PathComparer();
        _probeCache = new BoundedLruCache<string, ImageProbeResult>(
            256,
            256,
            _ => 1,
            pathComparer);
        Diagnostic = new DiagnosticErrorViewModel(clipboard);

        BackCommand = new RelayCommand<object?>(_ => NavigateHome());
        SelectItemCommand = new AsyncCommand<BrowserItemViewModel>(SelectItemAsync);
        PreviousCommand = new AsyncCommand(cancellationToken => MoveAsync(-1, cancellationToken), () => CanGoPrevious);
        NextCommand = new AsyncCommand(cancellationToken => MoveAsync(1, cancellationToken), () => CanGoNext);
        RemoveUnavailableCommand = new RelayCommand<BrowserItemViewModel>(RemoveUnavailable, item => item.IsUnavailable);
        CompressCommand = new RelayCommand<object?>(_ => NavigateToFeature(DesktopRoute.Compress), _ => CanCompress);
        ConvertCommand = new RelayCommand<object?>(_ => NavigateToFeature(DesktopRoute.Convert), _ => CanConvert);
        ResizeCommand = new RelayCommand<object?>(_ => NavigateToFeature(DesktopRoute.Resize), _ => CanResize);
        CropCommand = new RelayCommand<object?>(_ => NavigateToFeature(DesktopRoute.Crop), _ => CanCrop);
        OpenDirectoryCommand = new AsyncCommand(OpenDirectoryAsync, () => DirectoryPath is not null || CurrentItem is not null);
        AddImagesCommand = new AsyncCommand(AddImagesAsync, () => _picker is not null && !_isInteractionLocked && State != DesktopContentState.Loading);
    }

    public DesktopContentState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(IsLoading));
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(IsReady));
                OnPropertyChanged(nameof(IsCurrentUnavailable));
                OnPropertyChanged(nameof(CanUseCurrentImage));
                NotifyNavigationState();
                NotifyCommands();
            }
        }
    }

    public bool IsLoading => State == DesktopContentState.Loading;

    public bool IsEmpty => State == DesktopContentState.Empty;

    public bool IsReady => State == DesktopContentState.Ready;

    public bool IsCurrentUnavailable => State == DesktopContentState.Failure && CurrentItem?.IsUnavailable == true;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public DiagnosticErrorViewModel Diagnostic { get; }

    public IReadOnlyList<BrowserItemViewModel> Items
    {
        get => _items;
        private set
        {
            if (SetProperty(ref _items, value))
            {
                // ImageGallery treats its descriptor set as the identity boundary for
                // safe image leases. Keep one stable ItemsSource snapshot per browser
                // collection instead of returning a fresh array on every binding read;
                // otherwise a Shell layout refresh can rebuild descriptors underneath
                // an already-ready image and TryAcquireCurrentImage must reject it.
                _galleryItems = value.Select(item => item.GalleryItem).ToArray();
                OnPropertyChanged(nameof(HasItems));
                OnPropertyChanged(nameof(GalleryItems));
                OnPropertyChanged(nameof(IsGalleryToolbarVisible));
                OnPropertyChanged(nameof(CanUseGalleryFilmstripNavigation));
                OnPropertyChanged(nameof(CurrentIndex));
                OnPropertyChanged(nameof(CurrentPositionText));
            }
        }
    }

    public bool HasItems => Items.Count > 0;

    public ImageGalleryLoadLimits GalleryLoadLimits { get; }

    public IReadOnlyList<ImageGalleryItemAdapter> GalleryItems => _galleryItems;

    public ImageGalleryItemAdapter? SelectedGalleryItem
    {
        get => CurrentItem?.GalleryItem;
        set
        {
            if (value is not null && !ReferenceEquals(value.Item, _currentItem))
            {
                CurrentItem = value.Item;
            }
        }
    }

    public int ActiveBatchIndex
    {
        get => _activeBatchIndex;
        private set => SetProperty(ref _activeBatchIndex, value);
    }

    public bool IsCropMode
    {
        get => _isCropMode;
        private set
        {
            if (SetProperty(ref _isCropMode, value))
            {
                OnPropertyChanged(nameof(ViewerBackgroundHex));
                OnPropertyChanged(nameof(GalleryMainImageMode));
                OnPropertyChanged(nameof(IsGalleryToolbarVisible));
            }
        }
    }

    public string ViewerBackgroundHex => IsCropMode ? "#F5F7FA" : "#FFFFFF";
    public ImageGalleryMainImageMode GalleryMainImageMode =>
        IsCropMode ? ImageGalleryMainImageMode.ResourceOnly : ImageGalleryMainImageMode.Presented;
    public bool IsGalleryToolbarVisible => HasItems && !IsCropMode;
    public bool CanUseGalleryFilmstripNavigation => HasItems && !IsInteractionLocked;
    public CropEditorViewModel? CropEditor
    {
        get => _cropEditor;
        private set => SetProperty(ref _cropEditor, value);
    }
    public BrowserItemViewModel? CurrentItem
    {
        get => _currentItem;
        set
        {
            if (!_isInteractionLocked && value is not null && !ReferenceEquals(_currentItem, value))
            {
                _ = SelectItemAsync(value, CancellationToken.None);
            }
        }
    }

    public LocalPath? DirectoryPath
    {
        get => _directoryPath;
        private set
        {
            if (SetProperty(ref _directoryPath, value))
            {
                OnPropertyChanged(nameof(CurrentSourceLabel));
                OpenDirectoryCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public string CurrentDisplayName => CurrentItem?.DisplayName ?? string.Empty;

    public string CurrentPath => CurrentItem?.Path.Value ?? string.Empty;

    public string CurrentSourceLabel
    {
        get
        {
            if (DirectoryPath is null)
            {
                return "单张图片";
            }

            var trimmed = Path.TrimEndingDirectorySeparator(DirectoryPath.Value.Value);
            return Path.GetFileName(trimmed) is { Length: > 0 } name ? name : trimmed;
        }
    }

    public string CurrentPositionText => CurrentIndex < 0 ? string.Empty : $"{CurrentIndex + 1} / {Items.Count}";

    public string CurrentDimensions => _currentProbe is null ? string.Empty : $"{_currentProbe.Width} × {_currentProbe.Height}";

    public string CurrentFormat => _currentProbe?.Format.ToString().ToUpperInvariant() ?? string.Empty;

    public string CurrentFileSize => _currentProbe is null ? string.Empty : FormatBytes(_currentProbe.FileSizeBytes);

    public string CurrentTransparency => _currentProbe is null
        ? string.Empty
        : _currentProbe.HasTransparency ? "有" : "无";

    public string CurrentFrameSummary => _currentProbe is null
        ? string.Empty
        : _currentProbe.FrameCount > 1 ? $"{_currentProbe.FrameCount} 帧" : "否";

    public string CurrentMetadata => _currentProbe is null
        ? string.Empty
        : _currentProbe.HasMetadata ? "包含拍摄信息" : "无拍摄信息";

    public string CurrentColorProfile => _currentProbe is null
        ? string.Empty
        : _currentProbe.HasColorProfile ? "包含 ICC 色彩配置" : "无 ICC 色彩配置";

    public bool CanUseCurrentImage => State == DesktopContentState.Ready && _currentProbe is not null;

    public bool CanCompress => CanUseCurrentImage
        && IsSingleFrameSupportedInput
        && TryMapOutputFormat(_currentProbe!.Format, out var outputFormat)
        && _capabilities.SupportedOutputFormats.Contains(outputFormat);

    public bool CanConvert => CanUseCurrentImage && IsSingleFrameSupportedInput;

    public bool CanResize => CanUseCurrentImage
        && IsSingleFrameSupportedInput
        && _capabilities.Resize?.SupportedSameFormatFormats.Contains(_currentProbe!.Format) == true;

    public bool CanCrop => CanUseCurrentImage
        && IsSingleFrameSupportedInput
        && _capabilities.Crop?.SupportedSameFormatFormats.Contains(_currentProbe!.Format) == true;

    public bool CanGoPrevious => !_isInteractionLocked && State != DesktopContentState.Loading && CurrentIndex > 0;

    public bool CanGoNext => !_isInteractionLocked && State != DesktopContentState.Loading && CurrentIndex >= 0 && CurrentIndex < Items.Count - 1;

    public bool IsInteractionLocked
    {
        get => _isInteractionLocked;
        private set => SetProperty(ref _isInteractionLocked, value);
    }

    public RelayCommand<object?> BackCommand { get; }

    public AsyncCommand<BrowserItemViewModel> SelectItemCommand { get; }

    public AsyncCommand PreviousCommand { get; }

    public AsyncCommand NextCommand { get; }

    public RelayCommand<BrowserItemViewModel> RemoveUnavailableCommand { get; }

    public RelayCommand<object?> CompressCommand { get; }

    public RelayCommand<object?> ConvertCommand { get; }

    public RelayCommand<object?> ResizeCommand { get; }

    public RelayCommand<object?> CropCommand { get; }

    public AsyncCommand OpenDirectoryCommand { get; }

    public AsyncCommand AddImagesCommand { get; }

    public async Task LoadAsync(BrowserNavigationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        CancelLoading();
        ClearSessionData();
        var generation = ++_generation;
        var loadCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loadCancellation = loadCancellation;

        DirectoryPath = context.DirectoryPath;
        Items = context.Items
            .Select(item => new BrowserItemViewModel(
                item.Path,
                item.DisplayName,
                RemoveUnavailable))
            .ToArray();
        ClearCurrent();
        ErrorMessage = null;
        Diagnostic.Clear();

        if (Items.Count == 0)
        {
            State = DesktopContentState.Empty;
            return;
        }

        var preferredIndex = context.PreferredPath is { } preferred
            ? FindIndex(Items, item => string.Equals(item.Path.Value, preferred.Value, StringComparison.OrdinalIgnoreCase))
            : 0;
        if (preferredIndex < 0)
        {
            preferredIndex = 0;
        }

        for (var offset = 0; offset < Items.Count; offset++)
        {
            var index = (preferredIndex + offset) % Items.Count;
            var probe = index == preferredIndex ? context.PreferredProbe : null;
            if (await LoadItemCoreAsync(Items[index], probe, generation, loadCancellation.Token))
            {
                return;
            }

            if (generation != _generation || loadCancellation.IsCancellationRequested)
            {
                return;
            }
        }

        State = DesktopContentState.Failure;
        ErrorMessage = "文件夹中没有可读取的图片。";
        Diagnostic.Clear();
    }

    public void Dispose()
    {
        CancelLoading();
        ClearSessionData();
    }

    public void EndSession() => ReleaseSession();

    private async Task SelectItemAsync(BrowserItemViewModel item, CancellationToken cancellationToken)
    {
        if (_isInteractionLocked) return;
        CancelLoading();
        var generation = ++_generation;
        var loadCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loadCancellation = loadCancellation;
        await LoadItemCoreAsync(item, null, generation, loadCancellation.Token);
    }

    private async Task MoveAsync(int offset, CancellationToken cancellationToken)
    {
        var targetIndex = CurrentIndex + offset;
        if (targetIndex < 0 || targetIndex >= Items.Count)
        {
            return;
        }

        await SelectItemAsync(Items[targetIndex], cancellationToken);
    }

    private async Task<bool> LoadItemCoreAsync(
        BrowserItemViewModel item,
        ImageProbeResult? knownProbe,
        int generation,
        CancellationToken cancellationToken)
    {
        SetCurrentItem(item);
        _currentProbe = null;
        NotifyCurrentDetails();
        item.Availability = BrowserItemAvailability.Pending;
        State = DesktopContentState.Loading;
        ErrorMessage = null;

        var probe = knownProbe ?? item.Probe;
        var pathKey = NormalizeCachePath(item.Path);
        if (probe is null)
        {
            lock (_cacheSync) _probeCache.TryGetValue(pathKey, out probe);
        }
        if (probe is null)
        {
            var open = await _openImage.ExecuteAsync(new OpenImageRequest(item.Path), cancellationToken);
            if (!open.Succeeded)
            {
                if (generation != _generation || cancellationToken.IsCancellationRequested)
                {
                    return false;
                }

                item.Availability = BrowserItemAvailability.Unavailable;
                SetWorkflowError(open.Error);
                State = DesktopContentState.Failure;
                NotifyCurrentDetails();
                return false;
            }

            probe = open.Value!.ProbeResult;
        }
        if (generation != _generation || cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        item.SetProbe(probe);
        lock (_cacheSync) _probeCache.Set(pathKey, probe);

        _currentProbe = probe;
        item.Availability = BrowserItemAvailability.Ready;
        State = DesktopContentState.Ready;
        NotifyCurrentDetails();
        return true;
    }

    private void NavigateHome()
    {
        ReleaseSession();
        _navigator.Navigate(new DesktopNavigationRequest(DesktopRoute.Browse));
    }

    private void NavigateToFeature(DesktopRoute route)
    {
        if (CurrentItem is null || _currentProbe is null)
        {
            return;
        }

        _navigator.Navigate(new DesktopNavigationRequest(
            route,
            new SingleImageNavigationContext(CurrentItem.Path, _currentProbe)));
    }

    private async Task OpenDirectoryAsync(CancellationToken cancellationToken)
    {
        var directory = DirectoryPath?.Value;
        if (directory is null && CurrentItem is not null)
        {
            directory = Path.GetDirectoryName(CurrentItem.Path.Value);
        }

        if (!string.IsNullOrWhiteSpace(directory))
        {
            if (!await _launcher.OpenDirectoryAsync(directory, cancellationToken))
            {
                ErrorMessage = "无法在系统文件管理器中打开当前目录。";
                Diagnostic.Clear();
            }
        }
    }

    private async Task AddImagesAsync(CancellationToken cancellationToken)
    {
        if (_picker is null) return;
        var selection = await _picker.PickImagesAsync(cancellationToken);
        if (selection.Status == DesktopSelectionStatus.Canceled) return;
        if (selection.Status != DesktopSelectionStatus.Selected)
        {
            ErrorMessage = DesktopErrorText.FromPicker(selection.ErrorMessage);
            return;
        }

        var comparer = PathComparer();
        var appended = new List<BrowserItemViewModel>();
        var known = new HashSet<string>(Items.Select(item => Path.GetFullPath(item.Path.Value)), comparer);
        foreach (var value in selection.Paths)
        {
            var path = new LocalPath(value);
            if (known.Add(Path.GetFullPath(path.Value)))
            {
                appended.Add(new BrowserItemViewModel(
                    path,
                    Path.GetFileName(path.Value),
                    RemoveUnavailable));
            }
        }

        if (appended.Count == 0) return;

        var hadItems = Items.Count > 0;
        Items = Items.Concat(appended).ToArray();
        NotifyNavigationState();
        NotifyCommands();
        if (!hadItems)
        {
            await SelectItemAsync(appended[0], cancellationToken);
        }
    }

    private void SetCurrentItem(BrowserItemViewModel item)
    {
        if (SetProperty(ref _currentItem, item, nameof(CurrentItem)))
        {
            NotifyCurrentDetails();
            OnPropertyChanged(nameof(SelectedGalleryItem));
            NotifyNavigationState();
            OpenDirectoryCommand.NotifyCanExecuteChanged();
        }
    }

    private void ClearCurrent()
    {
        _currentItem = null;
        _currentProbe = null;
        OnPropertyChanged(nameof(CurrentItem));
        OnPropertyChanged(nameof(SelectedGalleryItem));
        NotifyCurrentDetails();
        NotifyNavigationState();
    }

    private void NotifyCurrentDetails()
    {
        OnPropertyChanged(nameof(CurrentDisplayName));
        OnPropertyChanged(nameof(CurrentPath));
        OnPropertyChanged(nameof(CurrentIndex));
        OnPropertyChanged(nameof(CurrentPositionText));
        OnPropertyChanged(nameof(CurrentDimensions));
        OnPropertyChanged(nameof(CurrentFormat));
        OnPropertyChanged(nameof(CurrentFileSize));
        OnPropertyChanged(nameof(CurrentTransparency));
        OnPropertyChanged(nameof(CurrentFrameSummary));
        OnPropertyChanged(nameof(CurrentMetadata));
        OnPropertyChanged(nameof(CurrentColorProfile));
        OnPropertyChanged(nameof(IsCurrentUnavailable));
        OnPropertyChanged(nameof(CanUseCurrentImage));
        OnPropertyChanged(nameof(CanCompress));
        OnPropertyChanged(nameof(CanConvert));
        OnPropertyChanged(nameof(CanResize));
        OnPropertyChanged(nameof(CanCrop));
        NotifyCommands();
    }

    private void NotifyCommands()
    {
        CompressCommand.NotifyCanExecuteChanged();
        ConvertCommand.NotifyCanExecuteChanged();
        ResizeCommand.NotifyCanExecuteChanged();
        CropCommand.NotifyCanExecuteChanged();
        AddImagesCommand.NotifyCanExecuteChanged();
    }

    public int CurrentIndex => CurrentItem is null ? -1 : FindIndex(Items, item => ReferenceEquals(item, CurrentItem));

    public bool TryCreateCurrentContext(out SingleImageNavigationContext? context)
    {
        if (CurrentItem is null || _currentProbe is null)
        {
            context = null;
            return false;
        }

        context = new SingleImageNavigationContext(CurrentItem.Path, _currentProbe);
        return true;
    }

    public void SetCropMode(bool value, CropEditorViewModel? editor = null)
    {
        CropEditor = value ? editor : null;
        IsCropMode = value;
    }

    public void SetInteractionLocked(bool value)
    {
        if (IsInteractionLocked == value) return;
        IsInteractionLocked = value;
        OnPropertyChanged(nameof(CanUseGalleryFilmstripNavigation));
        NotifyNavigationState();
        NotifyCommands();
    }

    public void ResetBatchStatuses()
    {
        foreach (var item in Items) item.TaskStatus = BrowserTaskStatus.None;
        ActiveBatchIndex = -1;
    }

    public void SetBatchStatus(LocalPath path, BrowserTaskStatus status)
    {
        var item = Items.FirstOrDefault(candidate => PathsEqual(candidate.Path, path));
        if (item is null) return;
        item.TaskStatus = status;
        ActiveBatchIndex = status == BrowserTaskStatus.Running
            ? FindIndex(Items, candidate => ReferenceEquals(candidate, item))
            : FindIndex(Items, candidate => candidate.TaskStatus == BrowserTaskStatus.Running);
    }

    private bool IsSingleFrameSupportedInput => _currentProbe is not null
        && _capabilities.SupportedInputFormats.Contains(_currentProbe.Format)
        && (!_currentProbe.IsAnimated || _capabilities.SupportsAnimatedImages)
        && _currentProbe.FrameCount == 1;

    private void RemoveUnavailable(BrowserItemViewModel item)
    {
        if (!item.IsUnavailable)
        {
            return;
        }

        var oldIndex = FindIndex(Items, candidate => ReferenceEquals(candidate, item));
        if (oldIndex < 0)
        {
            return;
        }

        var remaining = Items.Where(candidate => !ReferenceEquals(candidate, item)).ToArray();
        var removedCurrent = ReferenceEquals(CurrentItem, item);
        Items = remaining;
        if (!removedCurrent)
        {
            NotifyNavigationState();
            return;
        }

        CancelLoading();
        ClearCurrent();
        ErrorMessage = null;
        Diagnostic.Clear();
        if (remaining.Length == 0)
        {
            State = DesktopContentState.Empty;
            return;
        }

        var nextIndex = Math.Min(oldIndex, remaining.Length - 1);
        _ = SelectItemAsync(remaining[nextIndex], CancellationToken.None);
    }

    private void NotifyNavigationState()
    {
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
        PreviousCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
    }

    private void CancelLoading()
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
    }

    private void ReleaseSession()
    {
        CancelLoading();
        ++_generation;
        ClearSessionData();
        State = DesktopContentState.Empty;
    }

    private void ClearSessionData()
    {
        Items = Array.Empty<BrowserItemViewModel>();
        ActiveBatchIndex = -1;
        IsCropMode = false;
        CropEditor = null;
        DirectoryPath = null;
        ClearCurrent();
        ErrorMessage = null;
        Diagnostic.Clear();
        lock (_cacheSync)
        {
            _probeCache.Clear();
        }
    }

    private static string NormalizeCachePath(LocalPath path) => Path.GetFullPath(path.Value);

    private static StringComparer PathComparer() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static bool PathsEqual(LocalPath left, LocalPath right) =>
        PathComparer().Equals(Path.GetFullPath(left.Value), Path.GetFullPath(right.Value));

    private void SetWorkflowError(AtomPixError? error)
    {
        ErrorMessage = DesktopErrorText.FromWorkflow(error);
        Diagnostic.Set(error);
    }

    private static bool TryMapOutputFormat(ImageFormatKind format, out OutputImageFormat outputFormat)
    {
        outputFormat = format switch
        {
            ImageFormatKind.Jpeg => OutputImageFormat.Jpeg,
            ImageFormatKind.Png => OutputImageFormat.Png,
            ImageFormatKind.WebP => OutputImageFormat.WebP,
            _ => default
        };
        return format is ImageFormatKind.Jpeg or ImageFormatKind.Png or ImageFormatKind.WebP;
    }

    private static int FindIndex<T>(IReadOnlyList<T> items, Func<T, bool> predicate)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (predicate(items[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static string FormatBytes(long bytes)
    {
        var value = (double)bytes;
        var units = new[] { "B", "KB", "MB", "GB" };
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
    }
}
