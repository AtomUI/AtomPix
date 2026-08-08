namespace AtomPix.Desktop.ViewModels;

using AtomPix.Core.ValueObjects;
using AtomPix.Core.Results;
using AtomPix.Core.Errors;
using AtomPix.Desktop.Navigation;
using AtomPix.Desktop.Platform;
using AtomPix.Core.Conversion;
using AtomPix.Imaging.Abstractions.Formats;
using AtomPix.Imaging.Abstractions.Processing;
using AtomPix.Workflows.Images;

public enum BrowserItemAvailability
{
    Pending,
    Ready,
    Unavailable
}

public sealed class BrowserItemViewModel : ObservableObject
{
    private ImageProbeResult? _probe;
    private readonly Func<BrowserItemViewModel, CancellationToken, Task> _loadThumbnail;
    private BrowserItemAvailability _availability;
    private byte[]? _thumbnailBytes;
    private bool _isThumbnailLoading;
    private string? _thumbnailError;

    public BrowserItemViewModel(
        LocalPath path,
        string displayName,
        Func<BrowserItemViewModel, CancellationToken, Task> loadThumbnail,
        Action<BrowserItemViewModel> removeUnavailable)
    {
        Path = path;
        DisplayName = displayName;
        _loadThumbnail = loadThumbnail ?? throw new ArgumentNullException(nameof(loadThumbnail));
        ArgumentNullException.ThrowIfNull(removeUnavailable);
        EnsureThumbnailCommand = new AsyncCommand(
            cancellationToken => _loadThumbnail(this, cancellationToken),
            () => ThumbnailBytes is null && !IsThumbnailLoading && Availability != BrowserItemAvailability.Unavailable);
        RemoveCommand = new RelayCommand<object?>(_ => removeUnavailable(this), _ => IsUnavailable);
    }

    public LocalPath Path { get; }

    public string DisplayName { get; }

    public BrowserItemAvailability Availability
    {
        get => _availability;
        internal set
        {
            if (SetProperty(ref _availability, value))
            {
                OnPropertyChanged(nameof(IsUnavailable));
                EnsureThumbnailCommand.NotifyCanExecuteChanged();
                RemoveCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsUnavailable => Availability == BrowserItemAvailability.Unavailable;
    public ImageProbeResult? Probe => _probe;

    public byte[]? ThumbnailBytes
    {
        get => _thumbnailBytes;
        internal set
        {
            if (SetProperty(ref _thumbnailBytes, value))
            {
                EnsureThumbnailCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsThumbnailLoading
    {
        get => _isThumbnailLoading;
        internal set
        {
            if (SetProperty(ref _isThumbnailLoading, value))
            {
                EnsureThumbnailCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string? ThumbnailError
    {
        get => _thumbnailError;
        internal set => SetProperty(ref _thumbnailError, value);
    }

    public AsyncCommand EnsureThumbnailCommand { get; }

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
    private readonly CreatePreviewWorkflow _createPreview;
    private readonly ImageProcessorCapabilities _capabilities;
    private readonly IDesktopNavigator _navigator;
    private readonly IDesktopLauncherService _launcher;
    private readonly SemaphoreSlim _thumbnailConcurrency = new(2, 2);
    private readonly BoundedLruCache<string, ImageProbeResult> _probeCache;
    private readonly BoundedLruCache<string, byte[]> _previewCache;
    private readonly BoundedLruCache<string, RetainedThumbnail> _thumbnailCache;
    private readonly object _cacheSync = new();
    private CancellationTokenSource? _loadCancellation;
    private CancellationTokenSource? _thumbnailCancellation;
    private int _generation;
    private int _collectionGeneration;
    private DesktopContentState _state = DesktopContentState.Empty;
    private IReadOnlyList<BrowserItemViewModel> _items = Array.Empty<BrowserItemViewModel>();
    private BrowserItemViewModel? _currentItem;
    private ImageProbeResult? _currentProbe;
    private byte[]? _previewBytes;
    private string? _errorMessage;
    private LocalPath? _directoryPath;
    private bool _isFitMode = true;
    private double _zoomPercent = 100;

    public ImageBrowserViewModel(
        OpenImageWorkflow openImage,
        CreatePreviewWorkflow createPreview,
        IImageProcessor imageProcessor,
        IDesktopNavigator navigator,
        IDesktopLauncherService launcher,
        IDesktopClipboardService clipboard,
        BrowserCacheOptions? cacheOptions = null)
    {
        _openImage = openImage ?? throw new ArgumentNullException(nameof(openImage));
        _createPreview = createPreview ?? throw new ArgumentNullException(nameof(createPreview));
        _capabilities = (imageProcessor ?? throw new ArgumentNullException(nameof(imageProcessor))).Capabilities;
        _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        cacheOptions ??= new BrowserCacheOptions();
        var pathComparer = PathComparer();
        _probeCache = new BoundedLruCache<string, ImageProbeResult>(
            cacheOptions.ProbeEntryLimit,
            cacheOptions.ProbeEntryLimit,
            _ => 1,
            pathComparer);
        _previewCache = new BoundedLruCache<string, byte[]>(
            cacheOptions.PreviewEntryLimit,
            cacheOptions.PreviewByteBudget,
            bytes => bytes.LongLength,
            pathComparer);
        _thumbnailCache = new BoundedLruCache<string, RetainedThumbnail>(
            cacheOptions.ThumbnailEntryLimit,
            cacheOptions.ThumbnailByteBudget,
            entry => entry.Bytes.LongLength,
            pathComparer);
        Diagnostic = new DiagnosticErrorViewModel(clipboard);

        BackCommand = new RelayCommand<object?>(_ => NavigateHome());
        SelectItemCommand = new AsyncCommand<BrowserItemViewModel>(SelectItemAsync);
        PreviousCommand = new AsyncCommand(cancellationToken => MoveAsync(-1, cancellationToken), () => CanGoPrevious);
        NextCommand = new AsyncCommand(cancellationToken => MoveAsync(1, cancellationToken), () => CanGoNext);
        RemoveUnavailableCommand = new RelayCommand<BrowserItemViewModel>(RemoveUnavailable, item => item.IsUnavailable);
        FitCommand = new RelayCommand<object?>(_ => SetFitMode());
        ActualSizeCommand = new AsyncCommand(LoadActualSizeAsync, () => CanUseCurrentImage);
        ZoomOutCommand = new RelayCommand<object?>(_ => SetZoom(ZoomPercent - 25), _ => CanZoomOut);
        ZoomInCommand = new RelayCommand<object?>(_ => SetZoom(ZoomPercent + 25), _ => CanZoomIn);
        CompressCommand = new RelayCommand<object?>(_ => NavigateToFeature(DesktopRoute.Compress), _ => CanCompress);
        ConvertCommand = new RelayCommand<object?>(_ => NavigateToFeature(DesktopRoute.Convert), _ => CanConvert);
        ResizeCommand = new RelayCommand<object?>(_ => NavigateToFeature(DesktopRoute.Resize), _ => CanResize);
        CropCommand = new RelayCommand<object?>(_ => NavigateToFeature(DesktopRoute.Crop), _ => CanCrop);
        OpenDirectoryCommand = new AsyncCommand(OpenDirectoryAsync, () => DirectoryPath is not null || CurrentItem is not null);
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
                OnPropertyChanged(nameof(CanUseCurrentImage));
                NotifyNavigationState();
                NotifyCommands();
            }
        }
    }

    public bool IsLoading => State == DesktopContentState.Loading;

    public bool IsEmpty => State == DesktopContentState.Empty;

    public bool IsReady => State == DesktopContentState.Ready;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public DiagnosticErrorViewModel Diagnostic { get; }

    public IReadOnlyList<BrowserItemViewModel> Items
    {
        get => _items;
        private set => SetProperty(ref _items, value);
    }

    public BrowserItemViewModel? CurrentItem
    {
        get => _currentItem;
        set
        {
            if (value is not null && !ReferenceEquals(_currentItem, value))
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
                OpenDirectoryCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public byte[]? PreviewBytes
    {
        get => _previewBytes;
        private set => SetProperty(ref _previewBytes, value);
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

    public string CurrentDimensions => _currentProbe is null ? string.Empty : $"{_currentProbe.Width} × {_currentProbe.Height}";

    public string CurrentFormat => _currentProbe?.Format.ToString().ToUpperInvariant() ?? string.Empty;

    public string CurrentFileSize => _currentProbe is null ? string.Empty : FormatBytes(_currentProbe.FileSizeBytes);

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

    public bool CanGoPrevious => State != DesktopContentState.Loading && CurrentIndex > 0;

    public bool CanGoNext => State != DesktopContentState.Loading && CurrentIndex >= 0 && CurrentIndex < Items.Count - 1;

    public bool IsFitMode
    {
        get => _isFitMode;
        private set
        {
            if (SetProperty(ref _isFitMode, value))
            {
                OnPropertyChanged(nameof(ZoomDisplayText));
            }
        }
    }

    public double ZoomPercent
    {
        get => _zoomPercent;
        private set
        {
            if (SetProperty(ref _zoomPercent, value))
            {
                OnPropertyChanged(nameof(ZoomScale));
                OnPropertyChanged(nameof(ZoomDisplayText));
                OnPropertyChanged(nameof(CanZoomIn));
                OnPropertyChanged(nameof(CanZoomOut));
                ZoomInCommand.NotifyCanExecuteChanged();
                ZoomOutCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public double ZoomScale => ZoomPercent / 100d;

    public string ZoomDisplayText => IsFitMode ? "适应窗口" : $"{ZoomPercent:0}%";

    public bool CanZoomIn => PreviewBytes is not null && (IsFitMode || ZoomPercent < 400);

    public bool CanZoomOut => PreviewBytes is not null && (IsFitMode || ZoomPercent > 25);

    public BrowserCacheSnapshot CacheSnapshot
    {
        get
        {
            lock (_cacheSync)
            {
                return new BrowserCacheSnapshot(
                    _previewCache.Count,
                    _previewCache.TotalSize,
                    _thumbnailCache.Count,
                    _thumbnailCache.TotalSize,
                    _probeCache.Count);
            }
        }
    }

    public RelayCommand<object?> BackCommand { get; }

    public AsyncCommand<BrowserItemViewModel> SelectItemCommand { get; }

    public AsyncCommand PreviousCommand { get; }

    public AsyncCommand NextCommand { get; }

    public RelayCommand<BrowserItemViewModel> RemoveUnavailableCommand { get; }

    public RelayCommand<object?> FitCommand { get; }

    public AsyncCommand ActualSizeCommand { get; }

    public RelayCommand<object?> ZoomOutCommand { get; }

    public RelayCommand<object?> ZoomInCommand { get; }

    public RelayCommand<object?> CompressCommand { get; }

    public RelayCommand<object?> ConvertCommand { get; }

    public RelayCommand<object?> ResizeCommand { get; }

    public RelayCommand<object?> CropCommand { get; }

    public AsyncCommand OpenDirectoryCommand { get; }

    public async Task LoadAsync(BrowserNavigationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        CancelLoading();
        CancelThumbnailLoading();
        ClearSessionData();
        var generation = ++_generation;
        var collectionGeneration = ++_collectionGeneration;
        var loadCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loadCancellation = loadCancellation;
        _thumbnailCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        DirectoryPath = context.DirectoryPath;
        Items = context.Items
            .Select(item => new BrowserItemViewModel(
                item.Path,
                item.DisplayName,
                (browserItem, requestCancellation) => EnsureThumbnailAsync(browserItem, collectionGeneration, requestCancellation),
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
        CancelThumbnailLoading();
        ClearSessionData();
        _thumbnailConcurrency.Dispose();
    }

    private async Task SelectItemAsync(BrowserItemViewModel item, CancellationToken cancellationToken)
    {
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

    private async Task LoadActualSizeAsync(CancellationToken cancellationToken)
    {
        if (CurrentItem is null || _currentProbe is null)
        {
            return;
        }

        var item = CurrentItem;
        var generation = _generation;
        var maxPixelSize = Math.Max(_currentProbe.Width, _currentProbe.Height);
        if (maxPixelSize <= 1600)
        {
            SetZoom(100);
            return;
        }

        State = DesktopContentState.Loading;
        ErrorMessage = null;
        var previewBytes = await GetPreviewBytesAsync(item.Path, maxPixelSize, cancellationToken);
        if (generation != _generation || cancellationToken.IsCancellationRequested || !ReferenceEquals(CurrentItem, item))
        {
            return;
        }

        if (!previewBytes.Succeeded)
        {
            SetWorkflowError(previewBytes.Error);
            State = DesktopContentState.Ready;
            return;
        }

        PreviewBytes = previewBytes.Value!;
        SetZoom(100);
        State = DesktopContentState.Ready;
    }

    private async Task<bool> LoadItemCoreAsync(
        BrowserItemViewModel item,
        ImageProbeResult? knownProbe,
        int generation,
        CancellationToken cancellationToken)
    {
        SetCurrentItem(item);
        item.Availability = BrowserItemAvailability.Pending;
        State = DesktopContentState.Loading;
        ErrorMessage = null;
        PreviewBytes = null;

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

        var previewBytes = await GetPreviewBytesAsync(item.Path, 1600, cancellationToken);
        if (generation != _generation || cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        if (!previewBytes.Succeeded)
        {
            item.Availability = BrowserItemAvailability.Unavailable;
            SetWorkflowError(previewBytes.Error);
            State = DesktopContentState.Failure;
            return false;
        }

        _currentProbe = probe;
        PreviewBytes = previewBytes.Value!;
        item.Availability = BrowserItemAvailability.Ready;
        SetFitMode();
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

        var pathSnapshot = CurrentItem.Path;
        var probeSnapshot = _currentProbe;
        ReleaseSession();
        _navigator.Navigate(new DesktopNavigationRequest(
            route,
            new SingleImageNavigationContext(pathSnapshot, probeSnapshot)));
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

    private void SetCurrentItem(BrowserItemViewModel item)
    {
        if (SetProperty(ref _currentItem, item, nameof(CurrentItem)))
        {
            NotifyCurrentDetails();
            NotifyNavigationState();
            OpenDirectoryCommand.NotifyCanExecuteChanged();
        }
    }

    private void ClearCurrent()
    {
        _currentItem = null;
        _currentProbe = null;
        PreviewBytes = null;
        OnPropertyChanged(nameof(CurrentItem));
        NotifyCurrentDetails();
        NotifyNavigationState();
    }

    private void NotifyCurrentDetails()
    {
        OnPropertyChanged(nameof(CurrentDisplayName));
        OnPropertyChanged(nameof(CurrentPath));
        OnPropertyChanged(nameof(CurrentDimensions));
        OnPropertyChanged(nameof(CurrentFormat));
        OnPropertyChanged(nameof(CurrentFileSize));
        OnPropertyChanged(nameof(CurrentMetadata));
        OnPropertyChanged(nameof(CurrentColorProfile));
        OnPropertyChanged(nameof(CanUseCurrentImage));
        OnPropertyChanged(nameof(CanCompress));
        OnPropertyChanged(nameof(CanConvert));
        OnPropertyChanged(nameof(CanResize));
        OnPropertyChanged(nameof(CanCrop));
        OnPropertyChanged(nameof(CanZoomIn));
        OnPropertyChanged(nameof(CanZoomOut));
        NotifyCommands();
    }

    private void NotifyCommands()
    {
        CompressCommand.NotifyCanExecuteChanged();
        ConvertCommand.NotifyCanExecuteChanged();
        ResizeCommand.NotifyCanExecuteChanged();
        CropCommand.NotifyCanExecuteChanged();
        ActualSizeCommand.NotifyCanExecuteChanged();
        ZoomInCommand.NotifyCanExecuteChanged();
        ZoomOutCommand.NotifyCanExecuteChanged();
    }

    private int CurrentIndex => CurrentItem is null ? -1 : FindIndex(Items, item => ReferenceEquals(item, CurrentItem));

    private bool IsSingleFrameSupportedInput => _currentProbe is not null
        && _capabilities.SupportedInputFormats.Contains(_currentProbe.Format)
        && (!_currentProbe.IsAnimated || _capabilities.SupportsAnimatedImages)
        && _currentProbe.FrameCount == 1;

    private async Task EnsureThumbnailAsync(
        BrowserItemViewModel item,
        int collectionGeneration,
        CancellationToken requestCancellation)
    {
        if (item.ThumbnailBytes is not null || item.IsUnavailable || collectionGeneration != _collectionGeneration)
        {
            return;
        }

        var collectionCancellation = _thumbnailCancellation;
        if (collectionCancellation is null)
        {
            return;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(requestCancellation, collectionCancellation.Token);
        var entered = false;
        try
        {
            item.IsThumbnailLoading = true;
            await _thumbnailConcurrency.WaitAsync(linked.Token);
            entered = true;
            if (item.ThumbnailBytes is not null || collectionGeneration != _collectionGeneration)
            {
                return;
            }

            var preview = await GetPreviewBytesAsync(item.Path, 240, linked.Token, cacheResult: false);
            if (collectionGeneration != _collectionGeneration || linked.IsCancellationRequested)
            {
                return;
            }

            if (preview.Succeeded)
            {
                item.ThumbnailBytes = preview.Value!;
                RetainThumbnail(item, preview.Value!);
                item.ThumbnailError = null;
            }
            else
            {
                item.Availability = BrowserItemAvailability.Unavailable;
                item.ThumbnailError = DesktopErrorText.FromWorkflow(preview.Error);
            }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
        }
        finally
        {
            if (entered)
            {
                _thumbnailConcurrency.Release();
            }

            item.IsThumbnailLoading = false;
        }
    }

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

    private void SetFitMode()
    {
        IsFitMode = true;
        OnPropertyChanged(nameof(CanZoomIn));
        OnPropertyChanged(nameof(CanZoomOut));
        ZoomInCommand.NotifyCanExecuteChanged();
        ZoomOutCommand.NotifyCanExecuteChanged();
    }

    private void SetZoom(double percent)
    {
        IsFitMode = false;
        ZoomPercent = Math.Clamp(percent, 25, 400);
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
        ActualSizeCommand.Cancel();
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
    }

    private void CancelThumbnailLoading()
    {
        _thumbnailCancellation?.Cancel();
        _thumbnailCancellation?.Dispose();
        _thumbnailCancellation = null;
    }

    private async Task<OperationResult<byte[]>> GetPreviewBytesAsync(
        LocalPath path,
        int maxPixelSize,
        CancellationToken cancellationToken,
        bool cacheResult = true)
    {
        var key = $"{NormalizeCachePath(path)}\0{maxPixelSize}";
        if (cacheResult)
        {
            lock (_cacheSync)
            {
                if (_previewCache.TryGetValue(key, out var cached))
                {
                    return OperationResult<byte[]>.Success(cached!);
                }
            }
        }

        var preview = await _createPreview.ExecuteAsync(
            new CreatePreviewRequest(path, maxPixelSize),
            cancellationToken);
        if (!preview.Succeeded)
        {
            return OperationResult<byte[]>.Failure(preview.Error!);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var bytes = preview.Value!.Preview.EncodedBytes;
        if (cacheResult)
        {
            lock (_cacheSync) _previewCache.Set(key, bytes);
        }
        return OperationResult<byte[]>.Success(bytes);
    }

    private void RetainThumbnail(BrowserItemViewModel item, byte[] bytes)
    {
        IReadOnlyList<RetainedThumbnail> evicted;
        lock (_cacheSync)
        {
            evicted = _thumbnailCache.Set(
                NormalizeCachePath(item.Path),
                new RetainedThumbnail(item, bytes));
        }

        foreach (var entry in evicted)
        {
            if (ReferenceEquals(entry.Item.ThumbnailBytes, entry.Bytes))
            {
                entry.Item.ThumbnailBytes = null;
            }
        }
    }

    private void ReleaseSession()
    {
        CancelLoading();
        CancelThumbnailLoading();
        ++_generation;
        ++_collectionGeneration;
        ClearSessionData();
        State = DesktopContentState.Empty;
    }

    private void ClearSessionData()
    {
        foreach (var item in Items)
        {
            item.ThumbnailBytes = null;
            item.ThumbnailError = null;
        }
        Items = Array.Empty<BrowserItemViewModel>();
        DirectoryPath = null;
        ClearCurrent();
        ErrorMessage = null;
        Diagnostic.Clear();
        lock (_cacheSync)
        {
            _probeCache.Clear();
            _previewCache.Clear();
            _thumbnailCache.Clear();
        }
    }

    private static string NormalizeCachePath(LocalPath path) => Path.GetFullPath(path.Value);

    private static StringComparer PathComparer() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

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

    private sealed record RetainedThumbnail(BrowserItemViewModel Item, byte[] Bytes);
}
