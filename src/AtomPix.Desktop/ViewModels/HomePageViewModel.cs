namespace AtomPix.Desktop.ViewModels;

using AtomPix.Core.Errors;
using AtomPix.Core.Ports;
using AtomPix.Core.ValueObjects;
using AtomPix.Desktop.Navigation;
using AtomPix.Desktop.Platform;
using AtomPix.Workflows.Images;
using AtomUI.City.Presentation;

public sealed class HomePageViewModel : ViewModelBase
{
    private readonly IDesktopPickerService _picker;
    private readonly IFileSystemService _fileSystem;
    private readonly OpenImageWorkflow _openImage;
    private readonly OpenFolderWorkflow _openFolder;
    private readonly IDesktopNavigator _navigator;
    private DesktopContentState _state = DesktopContentState.Ready;
    private string? _errorMessage;
    private bool _isDragOver;

    public HomePageViewModel(
        IDesktopPickerService picker,
        IFileSystemService fileSystem,
        OpenImageWorkflow openImage,
        OpenFolderWorkflow openFolder,
        IDesktopNavigator navigator,
        IDesktopClipboardService clipboard,
        IUiCommandFactory? commandFactory = null)
    {
        _picker = picker ?? throw new ArgumentNullException(nameof(picker));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _openImage = openImage ?? throw new ArgumentNullException(nameof(openImage));
        _openFolder = openFolder ?? throw new ArgumentNullException(nameof(openFolder));
        _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        var commands = commandFactory ?? HeadlessUiCommandFactory.Instance;
        var activationOwner = commandFactory is null ? null : this;
        Diagnostic = new DiagnosticErrorViewModel(clipboard, commands);

        OpenImageCommand = commands.CreateAsync(OpenImageAsync, CanOpenSource, activationOwner: activationOwner);
        OpenFolderCommand = commands.CreateAsync(OpenFolderAsync, CanOpenSource, activationOwner: activationOwner);
        OpenForCompressCommand = commands.CreateAsync(
            token => OpenForFeatureAsync(DesktopRoute.Compress, token),
            CanOpenSource,
            activationOwner: activationOwner);
        OpenForConvertCommand = commands.CreateAsync(
            token => OpenForFeatureAsync(DesktopRoute.Convert, token),
            CanOpenSource,
            activationOwner: activationOwner);
        OpenForResizeCommand = commands.CreateAsync(
            token => OpenForFeatureAsync(DesktopRoute.Resize, token),
            CanOpenSource,
            activationOwner: activationOwner);
        OpenForCropCommand = commands.CreateAsync(
            token => OpenForFeatureAsync(DesktopRoute.Crop, token),
            CanOpenSource,
            activationOwner: activationOwner);
        OpenDroppedSourcesCommand = commands.CreateAsync<IReadOnlyList<string>>(
            (paths, token) => paths is null ? Task.CompletedTask : OpenDroppedSourcesAsync(paths, token),
            _ => CanOpenSource(),
            activationOwner: activationOwner);
    }

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

    public IAsyncRelayCommand OpenImageCommand { get; }

    public IAsyncRelayCommand OpenFolderCommand { get; }

    public IAsyncRelayCommand OpenForCompressCommand { get; }

    public IAsyncRelayCommand OpenForConvertCommand { get; }

    public IAsyncRelayCommand OpenForResizeCommand { get; }

    public IAsyncRelayCommand OpenForCropCommand { get; }

    public IAsyncRelayCommand<IReadOnlyList<string>> OpenDroppedSourcesCommand { get; }

    public void SetDragOver(bool value)
    {
        IsDragOver = value && CanOpenSource();
    }

    public void ReportDropFailure()
    {
        IsDragOver = false;
        Fail("无法读取拖入的图片或文件夹，请改用打开按钮重试。");
    }

    private bool CanOpenSource() =>
        State != DesktopContentState.Loading && !_navigator.IsNavigationLocked;

    private async Task OpenImageAsync(CancellationToken cancellationToken)
    {
        var context = await PickImageCollectionAsync(cancellationToken);
        if (context is not null)
        {
            _navigator.Navigate(new DesktopNavigationRequest(DesktopRoute.Browse, context));
        }
    }

    private async Task OpenDroppedSourcesAsync(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken)
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

    private async Task OpenForFeatureAsync(
        DesktopRoute route,
        CancellationToken cancellationToken)
    {
        var context = await PickImageCollectionAsync(cancellationToken);
        if (context is not null)
        {
            _navigator.Navigate(new DesktopNavigationRequest(
                route,
                new BrowserToolNavigationContext(context)));
        }
    }

    private async Task<BrowserNavigationContext?> PickImageCollectionAsync(
        CancellationToken cancellationToken)
    {
        BeginLoading();
        var selection = await _picker.PickImagesAsync(cancellationToken);
        if (selection.Status == DesktopSelectionStatus.Canceled)
        {
            State = DesktopContentState.Ready;
            return null;
        }

        if (selection.Status != DesktopSelectionStatus.Selected || selection.Paths.Count == 0)
        {
            Fail(DesktopErrorText.FromPicker(selection.ErrorMessage));
            return null;
        }

        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var known = new HashSet<string>(comparer);
        var candidates = new List<BrowserImageCandidate>(selection.Paths.Count);
        foreach (var selectedPath in selection.Paths)
        {
            try
            {
                var path = new LocalPath(selectedPath);
                var normalized = Path.GetFullPath(path.Value);
                if (known.Add(normalized))
                {
                    candidates.Add(new BrowserImageCandidate(path, Path.GetFileName(path.Value)));
                }
            }
            catch (Exception exception)
                when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
            }
        }

        if (candidates.Count == 0)
        {
            Fail("没有可打开的图片路径，请重新选择。");
            return null;
        }

        State = DesktopContentState.Ready;
        return new BrowserNavigationContext(null, candidates);
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

    private async Task OpenKnownImageAsync(
        LocalPath path,
        CancellationToken cancellationToken)
    {
        var result = await _openImage.ExecuteAsync(new OpenImageRequest(path), cancellationToken);
        if (!result.Succeeded)
        {
            SetWorkflowError(result.Error);
            return;
        }

        State = DesktopContentState.Ready;
        _navigator.Navigate(new DesktopNavigationRequest(
            DesktopRoute.Browse,
            new BrowserNavigationContext(
                null,
                [new BrowserImageCandidate(path, Path.GetFileName(path.Value))],
                path,
                result.Value!.ProbeResult)));
    }

    private async Task OpenKnownFolderAsync(
        LocalPath path,
        CancellationToken cancellationToken)
    {
        var result = await _openFolder.ExecuteAsync(new OpenFolderRequest(path), cancellationToken);
        if (!result.Succeeded)
        {
            SetWorkflowError(result.Error);
            return;
        }

        State = DesktopContentState.Ready;
        _navigator.Navigate(new DesktopNavigationRequest(
            DesktopRoute.Browse,
            new BrowserNavigationContext(result.Value!.DirectoryPath, result.Value.Items)));
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
        var message = DesktopErrorText.FromWorkflow(error);
        if (string.IsNullOrEmpty(message))
        {
            State = DesktopContentState.Ready;
            return;
        }

        ErrorMessage = message;
        Diagnostic.Set(error);
        State = DesktopContentState.Failure;
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
    }
}
