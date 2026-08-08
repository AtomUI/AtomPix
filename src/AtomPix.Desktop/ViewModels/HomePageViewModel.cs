namespace AtomPix.Desktop.ViewModels;

using System.Collections.ObjectModel;
using AtomPix.Core.Ports;
using AtomPix.Core.Errors;
using AtomPix.Core.Settings;
using AtomPix.Core.ValueObjects;
using AtomPix.Desktop.Navigation;
using AtomPix.Desktop.Platform;
using AtomPix.Workflows.Images;
using AtomPix.Workflows.RecentItems;
using AtomPix.Workflows.Settings;

public sealed class RecentItemViewModel : ObservableObject
{
    private bool _isOpening;
    private bool _isUnavailable;

    public RecentItemViewModel(RecentItem item) => Item = item ?? throw new ArgumentNullException(nameof(item));

    public RecentItem Item { get; }
    public string Name => Item.Kind == RecentItemKind.Directory
        ? new DirectoryInfo(Item.Path.Value).Name
        : Path.GetFileName(Item.Path.Value);
    public string PathText => Item.Path.Value;
    public string KindText => Item.Kind == RecentItemKind.Directory ? "文件夹" : "图片";
    public string OpenedText => Item.OpenedAt.LocalDateTime.ToString("MM-dd HH:mm");
    public bool IsOpening { get => _isOpening; set => SetProperty(ref _isOpening, value); }
    public bool IsUnavailable { get => _isUnavailable; set => SetProperty(ref _isUnavailable, value); }
}

public sealed class HomePageViewModel : ObservableObject
{
    private readonly IDesktopPickerService _picker;
    private readonly IFileSystemService _fileSystem;
    private readonly OpenImageWorkflow _openImage;
    private readonly OpenFolderWorkflow _openFolder;
    private readonly IDesktopNavigator _navigator;
    private readonly LoadSettingsWorkflow _loadSettings;
    private readonly LoadRecentItemsWorkflow _loadRecentItems;
    private readonly AddRecentItemWorkflow _addRecentItem;
    private readonly RemoveRecentItemWorkflow _removeRecentItem;
    private readonly ClearRecentItemsWorkflow _clearRecentItems;
    private readonly IDesktopDialogService _dialogs;
    private DesktopContentState _state = DesktopContentState.Ready;
    private DesktopContentState _recentState = DesktopContentState.Empty;
    private string? _errorMessage;
    private string? _recentErrorMessage;
    private bool _recentInitialized;
    private bool _recentEnabled = true;
    private int _recentMaxCount = 20;
    private bool _isRecentDrawerOpen;
    private bool _isDragOver;

    public HomePageViewModel(
        IDesktopPickerService picker,
        IFileSystemService fileSystem,
        OpenImageWorkflow openImage,
        OpenFolderWorkflow openFolder,
        IDesktopNavigator navigator,
        LoadSettingsWorkflow loadSettings,
        LoadRecentItemsWorkflow loadRecentItems,
        AddRecentItemWorkflow addRecentItem,
        RemoveRecentItemWorkflow removeRecentItem,
        ClearRecentItemsWorkflow clearRecentItems,
        IDesktopDialogService dialogs,
        IDesktopClipboardService clipboard)
    {
        _picker = picker ?? throw new ArgumentNullException(nameof(picker));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _openImage = openImage ?? throw new ArgumentNullException(nameof(openImage));
        _openFolder = openFolder ?? throw new ArgumentNullException(nameof(openFolder));
        _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        _loadSettings = loadSettings ?? throw new ArgumentNullException(nameof(loadSettings));
        _loadRecentItems = loadRecentItems ?? throw new ArgumentNullException(nameof(loadRecentItems));
        _addRecentItem = addRecentItem ?? throw new ArgumentNullException(nameof(addRecentItem));
        _removeRecentItem = removeRecentItem ?? throw new ArgumentNullException(nameof(removeRecentItem));
        _clearRecentItems = clearRecentItems ?? throw new ArgumentNullException(nameof(clearRecentItems));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        Diagnostic = new DiagnosticErrorViewModel(clipboard);

        OpenImageCommand = new AsyncCommand(OpenImageAsync, CanOpenSource);
        OpenFolderCommand = new AsyncCommand(OpenFolderAsync, CanOpenSource);
        OpenForCompressCommand = new AsyncCommand(token => OpenForFeatureAsync(DesktopRoute.Compress, token), CanOpenSource);
        OpenForConvertCommand = new AsyncCommand(token => OpenForFeatureAsync(DesktopRoute.Convert, token), CanOpenSource);
        OpenForResizeCommand = new AsyncCommand(token => OpenForFeatureAsync(DesktopRoute.Resize, token), CanOpenSource);
        OpenForCropCommand = new AsyncCommand(token => OpenForFeatureAsync(DesktopRoute.Crop, token), CanOpenSource);
        OpenDroppedSourcesCommand = new AsyncCommand<IReadOnlyList<string>>(OpenDroppedSourcesAsync, _ => CanOpenSource());
        OpenRecentCommand = new AsyncCommand<RecentItemViewModel>(OpenRecentAsync, item => item is not null && !IsBusy);
        RemoveRecentCommand = new AsyncCommand<RecentItemViewModel>(RemoveRecentAsync, item => item is not null && RecentState == DesktopContentState.Ready);
        RelocateRecentCommand = new AsyncCommand<RecentItemViewModel>(RelocateRecentAsync, item => item is { IsUnavailable: true } && !IsBusy);
        ShowAllRecentCommand = new RelayCommand<object?>(_ => IsRecentDrawerOpen = true, _ => HasRecentItems && RecentState == DesktopContentState.Ready);
        ClearRecentCommand = new AsyncCommand(ClearRecentAsync, () => HasRecentItems && RecentState == DesktopContentState.Ready);
    }

    public ObservableCollection<RecentItemViewModel> RecentItems { get; } = [];
    public DiagnosticErrorViewModel Diagnostic { get; }

    public DesktopContentState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(CanAcceptDrop));
                NotifyCommands();
            }
        }
    }

    public bool IsBusy => State == DesktopContentState.Loading;

    public bool CanAcceptDrop => CanOpenSource();

    public bool IsDragOver
    {
        get => _isDragOver;
        private set => SetProperty(ref _isDragOver, value);
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public DesktopContentState RecentState
    {
        get => _recentState;
        private set
        {
            if (SetProperty(ref _recentState, value))
            {
                OnPropertyChanged(nameof(IsRecentLoading));
                OnPropertyChanged(nameof(IsRecentReady));
                NotifyRecentCommands();
            }
        }
    }

    public bool IsRecentLoading => RecentState == DesktopContentState.Loading;
    public bool IsRecentReady => RecentState == DesktopContentState.Ready;
    public bool HasRecentItems => RecentItems.Count > 0;
    public bool IsRecentEmpty => IsRecentReady && !HasRecentItems;
    public bool HasRecentError => !string.IsNullOrWhiteSpace(RecentErrorMessage);
    public string? RecentErrorMessage
    {
        get => _recentErrorMessage;
        private set { if (SetProperty(ref _recentErrorMessage, value)) OnPropertyChanged(nameof(HasRecentError)); }
    }

    public bool IsRecentDrawerOpen
    {
        get => _isRecentDrawerOpen;
        set => SetProperty(ref _isRecentDrawerOpen, value);
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

    public AsyncCommand OpenImageCommand { get; }

    public AsyncCommand OpenFolderCommand { get; }

    public AsyncCommand OpenForCompressCommand { get; }

    public AsyncCommand OpenForConvertCommand { get; }

    public AsyncCommand OpenForResizeCommand { get; }

    public AsyncCommand OpenForCropCommand { get; }
    public AsyncCommand<IReadOnlyList<string>> OpenDroppedSourcesCommand { get; }
    public AsyncCommand<RecentItemViewModel> OpenRecentCommand { get; }
    public AsyncCommand<RecentItemViewModel> RemoveRecentCommand { get; }
    public AsyncCommand<RecentItemViewModel> RelocateRecentCommand { get; }
    public RelayCommand<object?> ShowAllRecentCommand { get; }
    public AsyncCommand ClearRecentCommand { get; }

    public async Task LoadRecentAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        if (_recentInitialized && !force) return;
        RecentState = DesktopContentState.Loading;
        RecentErrorMessage = null;
        Diagnostic.Clear();

        var settings = await _loadSettings.ExecuteAsync(new LoadSettingsRequest(), cancellationToken);
        if (!settings.Succeeded)
        {
            SetRecentWorkflowError(settings.Error);
            RecentState = DesktopContentState.Failure;
            return;
        }

        _recentEnabled = settings.Value!.Settings.RecentItems.Enabled;
        _recentMaxCount = settings.Value.Settings.RecentItems.MaxCount;
        if (!_recentEnabled)
        {
            ReplaceRecentItems([]);
            _recentInitialized = true;
            RecentState = DesktopContentState.Ready;
            return;
        }

        var recent = await _loadRecentItems.ExecuteAsync(new LoadRecentItemsRequest(_recentMaxCount), cancellationToken);
        if (!recent.Succeeded)
        {
            SetRecentWorkflowError(recent.Error);
            RecentState = DesktopContentState.Failure;
            return;
        }

        ReplaceRecentItems(recent.Value!.Items);
        _recentInitialized = true;
        RecentState = DesktopContentState.Ready;
    }

    private bool CanOpenSource() => State != DesktopContentState.Loading && !_navigator.IsNavigationLocked;

    private async Task OpenImageAsync(CancellationToken cancellationToken)
    {
        var opened = await PickAndProbeImageAsync(cancellationToken);
        if (opened is null)
        {
            return;
        }

        var item = new BrowserImageCandidate(opened.Value.Path, Path.GetFileName(opened.Value.Path.Value));
        await RecordRecentAsync(opened.Value.Path, RecentItemKind.File, cancellationToken);
        _navigator.Navigate(new DesktopNavigationRequest(
            DesktopRoute.Browse,
            new BrowserNavigationContext(null, [item], opened.Value.Path, opened.Value.Probe)));
    }

    public void SetDragOver(bool value)
    {
        IsDragOver = value && CanOpenSource();
    }

    public void ReportDropFailure()
    {
        IsDragOver = false;
        Fail("无法读取拖入的图片或文件夹，请改用打开按钮重试。");
    }

    private async Task OpenDroppedSourcesAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        IsDragOver = false;
        BeginLoading();
        if (paths is null || paths.Count != 1 || string.IsNullOrWhiteSpace(paths[0]))
        {
            Fail("一次只能拖入一张图片或一个文件夹。");
            return;
        }

        LocalPath path;
        try
        {
            path = new LocalPath(paths[0]);
        }
        catch (ArgumentException)
        {
            Fail("拖入的路径无效，请重新选择图片或文件夹。");
            return;
        }

        if (_fileSystem.FileExists(path))
        {
            await OpenKnownImageAsync(path, cancellationToken);
            return;
        }

        if (_fileSystem.DirectoryExists(path))
        {
            await OpenKnownFolderAsync(path, cancellationToken);
            return;
        }

        Fail("拖入的图片或文件夹已不存在，或当前无法访问。");
    }

    private async Task OpenForFeatureAsync(DesktopRoute route, CancellationToken cancellationToken)
    {
        var opened = await PickAndProbeImageAsync(cancellationToken);
        if (opened is null)
        {
            return;
        }

        await RecordRecentAsync(opened.Value.Path, RecentItemKind.File, cancellationToken);

        _navigator.Navigate(new DesktopNavigationRequest(
            route,
            new SingleImageNavigationContext(opened.Value.Path, opened.Value.Probe)));
    }

    private async Task<(LocalPath Path, AtomPix.Imaging.Abstractions.Processing.ImageProbeResult Probe)?> PickAndProbeImageAsync(
        CancellationToken cancellationToken)
    {
        BeginLoading();
        var selection = await _picker.PickSingleImageAsync(cancellationToken);
        if (selection.Status == DesktopSelectionStatus.Canceled)
        {
            State = DesktopContentState.Ready;
            return null;
        }

        if (selection.Status != DesktopSelectionStatus.Selected || selection.Paths.Count != 1)
        {
            Fail(DesktopErrorText.FromPicker(selection.ErrorMessage));
            return null;
        }

        var path = new LocalPath(selection.Paths[0]);
        var result = await _openImage.ExecuteAsync(new OpenImageRequest(path), cancellationToken);
        if (!result.Succeeded)
        {
            var error = DesktopErrorText.FromWorkflow(result.Error);
            if (string.IsNullOrEmpty(error))
            {
                State = DesktopContentState.Ready;
            }
            else
            {
                Fail(error);
                Diagnostic.Set(result.Error);
            }

            return null;
        }

        State = DesktopContentState.Ready;
        return (path, result.Value!.ProbeResult);
    }

    private async Task OpenFolderAsync(CancellationToken cancellationToken)
    {
        BeginLoading();
        var selection = await _picker.PickFolderAsync(cancellationToken);
        if (selection.Status == DesktopSelectionStatus.Canceled)
        {
            State = DesktopContentState.Ready;
            return;
        }

        if (selection.Status != DesktopSelectionStatus.Selected || selection.Paths.Count != 1)
        {
            Fail(DesktopErrorText.FromPicker(selection.ErrorMessage));
            return;
        }

        await OpenKnownFolderAsync(new LocalPath(selection.Paths[0]), cancellationToken);
    }

    private async Task OpenKnownImageAsync(LocalPath path, CancellationToken cancellationToken)
    {
        var result = await _openImage.ExecuteAsync(new OpenImageRequest(path), cancellationToken);
        if (!result.Succeeded)
        {
            var error = DesktopErrorText.FromWorkflow(result.Error);
            if (string.IsNullOrEmpty(error))
            {
                State = DesktopContentState.Ready;
            }
            else
            {
                Fail(error);
                Diagnostic.Set(result.Error);
            }

            return;
        }

        State = DesktopContentState.Ready;
        await RecordRecentAsync(path, RecentItemKind.File, cancellationToken);
        _navigator.Navigate(new DesktopNavigationRequest(
            DesktopRoute.Browse,
            new BrowserNavigationContext(
                null,
                [new BrowserImageCandidate(path, Path.GetFileName(path.Value))],
                path,
                result.Value!.ProbeResult)));
    }

    private async Task OpenKnownFolderAsync(LocalPath path, CancellationToken cancellationToken)
    {
        var result = await _openFolder.ExecuteAsync(new OpenFolderRequest(path), cancellationToken);
        if (!result.Succeeded)
        {
            var error = DesktopErrorText.FromWorkflow(result.Error);
            if (string.IsNullOrEmpty(error)) State = DesktopContentState.Ready;
            else { Fail(error); Diagnostic.Set(result.Error); }
            return;
        }

        State = DesktopContentState.Ready;
        await RecordRecentAsync(result.Value!.DirectoryPath, RecentItemKind.Directory, cancellationToken);
        _navigator.Navigate(new DesktopNavigationRequest(
            DesktopRoute.Browse,
            new BrowserNavigationContext(result.Value.DirectoryPath, result.Value.Items)));
    }

    private async Task OpenRecentAsync(RecentItemViewModel item, CancellationToken cancellationToken)
    {
        item.IsOpening = true;
        ErrorMessage = null;
        Diagnostic.Clear();
        try
        {
            if (item.Item.Kind == RecentItemKind.File)
            {
                var opened = await _openImage.ExecuteAsync(new OpenImageRequest(item.Item.Path), cancellationToken);
                if (!opened.Succeeded) { item.IsUnavailable = true; SetWorkflowError(opened.Error); return; }
                item.IsUnavailable = false;
                await RecordRecentAsync(item.Item.Path, RecentItemKind.File, cancellationToken);
                _navigator.Navigate(new DesktopNavigationRequest(
                    DesktopRoute.Browse,
                    new BrowserNavigationContext(
                        null,
                        [new BrowserImageCandidate(item.Item.Path, Path.GetFileName(item.Item.Path.Value))],
                        item.Item.Path,
                        opened.Value!.ProbeResult)));
                return;
            }

            var folder = await _openFolder.ExecuteAsync(new OpenFolderRequest(item.Item.Path), cancellationToken);
            if (!folder.Succeeded) { item.IsUnavailable = true; SetWorkflowError(folder.Error); return; }
            item.IsUnavailable = false;
            await RecordRecentAsync(item.Item.Path, RecentItemKind.Directory, cancellationToken);
            _navigator.Navigate(new DesktopNavigationRequest(
                DesktopRoute.Browse,
                new BrowserNavigationContext(folder.Value!.DirectoryPath, folder.Value.Items)));
        }
        finally
        {
            item.IsOpening = false;
        }
    }

    private async Task RemoveRecentAsync(RecentItemViewModel item, CancellationToken cancellationToken)
    {
        var result = await _removeRecentItem.ExecuteAsync(
            new RemoveRecentItemRequest(item.Item.Path, item.Item.Kind),
            cancellationToken);
        if (!result.Succeeded)
        {
            SetRecentWorkflowError(result.Error);
            return;
        }

        ReplaceRecentItems(result.Value!.Items);
    }

    private async Task RelocateRecentAsync(RecentItemViewModel item, CancellationToken cancellationToken)
    {
        var selection = item.Item.Kind == RecentItemKind.File
            ? await _picker.PickSingleImageAsync(cancellationToken)
            : await _picker.PickFolderAsync(cancellationToken);
        if (selection.Status == DesktopSelectionStatus.Canceled)
        {
            return;
        }

        if (selection.Status != DesktopSelectionStatus.Selected || selection.Paths.Count != 1)
        {
            ErrorMessage = DesktopErrorText.FromPicker(selection.ErrorMessage);
            Diagnostic.Clear();
            return;
        }

        var replacement = new LocalPath(selection.Paths[0]);
        if (item.Item.Kind == RecentItemKind.File)
        {
            var opened = await _openImage.ExecuteAsync(new OpenImageRequest(replacement), cancellationToken);
            if (!opened.Succeeded) { SetWorkflowError(opened.Error); return; }
            if (!await RemoveStaleRecentAsync(item, cancellationToken)) return;
            await RecordRecentAsync(replacement, RecentItemKind.File, cancellationToken);
            _navigator.Navigate(new DesktopNavigationRequest(
                DesktopRoute.Browse,
                new BrowserNavigationContext(
                    null,
                    [new BrowserImageCandidate(replacement, Path.GetFileName(replacement.Value))],
                    replacement,
                    opened.Value!.ProbeResult)));
            return;
        }

        var folder = await _openFolder.ExecuteAsync(new OpenFolderRequest(replacement), cancellationToken);
        if (!folder.Succeeded) { SetWorkflowError(folder.Error); return; }
        if (!await RemoveStaleRecentAsync(item, cancellationToken)) return;
        await RecordRecentAsync(replacement, RecentItemKind.Directory, cancellationToken);
        _navigator.Navigate(new DesktopNavigationRequest(
            DesktopRoute.Browse,
            new BrowserNavigationContext(folder.Value!.DirectoryPath, folder.Value.Items)));
    }

    private async Task<bool> RemoveStaleRecentAsync(RecentItemViewModel item, CancellationToken cancellationToken)
    {
        var removed = await _removeRecentItem.ExecuteAsync(
            new RemoveRecentItemRequest(item.Item.Path, item.Item.Kind),
            cancellationToken);
        if (!removed.Succeeded)
        {
            SetRecentWorkflowError(removed.Error);
            return false;
        }

        ReplaceRecentItems(removed.Value!.Items);
        return true;
    }

    private async Task ClearRecentAsync(CancellationToken cancellationToken)
    {
        var confirmed = await _dialogs.ConfirmAsync(
            "清空最近记录？",
            "只会清除 AtomPix 的本地最近记录，不会删除任何图片或文件夹。",
            "清空记录",
            "取消",
            cancellationToken);
        if (!confirmed) return;

        var result = await _clearRecentItems.ExecuteAsync(new ClearRecentItemsRequest(), cancellationToken);
        if (!result.Succeeded)
        {
            SetRecentWorkflowError(result.Error);
            return;
        }

        ReplaceRecentItems([]);
        IsRecentDrawerOpen = false;
    }

    private async Task RecordRecentAsync(LocalPath path, RecentItemKind kind, CancellationToken cancellationToken)
    {
        if (!_recentInitialized) await LoadRecentAsync(cancellationToken: cancellationToken);
        if (!_recentInitialized || !_recentEnabled) return;

        var result = await _addRecentItem.ExecuteAsync(
            new AddRecentItemRequest(path, kind, DateTimeOffset.UtcNow, _recentMaxCount),
            cancellationToken);
        if (result.Succeeded) ReplaceRecentItems(result.Value!.Items);
        else SetRecentWorkflowError(result.Error);
    }

    private void ReplaceRecentItems(IReadOnlyList<RecentItem> items)
    {
        RecentItems.Clear();
        foreach (var item in items) RecentItems.Add(new RecentItemViewModel(item));
        OnPropertyChanged(nameof(HasRecentItems));
        OnPropertyChanged(nameof(IsRecentEmpty));
        NotifyRecentCommands();
    }

    private void BeginLoading()
    {
        ErrorMessage = null;
        Diagnostic.Clear();
        State = DesktopContentState.Loading;
    }

    private void Fail(string message)
    {
        ErrorMessage = message;
        Diagnostic.Clear();
        State = DesktopContentState.Failure;
    }

    private void SetWorkflowError(AtomPixError? error)
    {
        ErrorMessage = DesktopErrorText.FromWorkflow(error);
        Diagnostic.Set(error);
    }

    private void SetRecentWorkflowError(AtomPixError? error)
    {
        RecentErrorMessage = DesktopErrorText.FromWorkflow(error);
        Diagnostic.Set(error);
    }

    private void NotifyCommands()
    {
        OpenImageCommand.NotifyCanExecuteChanged();
        OpenFolderCommand.NotifyCanExecuteChanged();
        OpenForCompressCommand.NotifyCanExecuteChanged();
        OpenForConvertCommand.NotifyCanExecuteChanged();
        OpenForResizeCommand.NotifyCanExecuteChanged();
        OpenForCropCommand.NotifyCanExecuteChanged();
        OpenDroppedSourcesCommand.NotifyCanExecuteChanged();
        OpenRecentCommand.NotifyCanExecuteChanged();
    }

    private void NotifyRecentCommands()
    {
        ShowAllRecentCommand.NotifyCanExecuteChanged();
        ClearRecentCommand.NotifyCanExecuteChanged();
        RemoveRecentCommand.NotifyCanExecuteChanged();
        RelocateRecentCommand.NotifyCanExecuteChanged();
        OpenRecentCommand.NotifyCanExecuteChanged();
    }
}
