namespace AtomPix.Desktop.ViewModels;

using AtomPix.Core.Compression;
using AtomPix.Core.Errors;
using AtomPix.Core.Jobs;
using AtomPix.Core.Output;
using AtomPix.Core.ValueObjects;
using AtomPix.Desktop.Navigation;
using AtomPix.Desktop.Platform;
using AtomPix.Imaging.Abstractions.Formats;
using AtomPix.Imaging.Abstractions.Processing;
using AtomPix.Workflows.Images;
using AtomPix.Workflows.Settings;

public sealed class CompressionEditorViewModel : ObservableObject, IDisposable, IDesktopForegroundTask, IResultAvailabilityAware
{
    private readonly IDesktopPickerService _picker;
    private readonly IDesktopLauncherService _launcher;
    private readonly IDesktopDialogService _dialogs;
    private readonly IDesktopClipboardService _clipboard;
    private readonly ResultOutputGuard _outputGuard;
    private readonly OpenImageWorkflow _openImage;
    private readonly CreatePreviewWorkflow _createPreview;
    private readonly LoadSettingsWorkflow _loadSettings;
    private readonly CompressImageWorkflow _compress;
    private readonly DesktopNavigationCoordinator _navigation;
    private CancellationTokenSource? _loadCancellation;
    private CancellationTokenSource? _executionCancellation;
    private int _loadGeneration;
    private DesktopContentState _contentState = DesktopContentState.Empty;
    private DesktopExecutionState _executionState = DesktopExecutionState.Draft;
    private LocalPath? _inputPath;
    private ImageProbeResult? _probe;
    private byte[]? _previewBytes;
    private DesktopChoiceOption<CompressionMode> _selectedMode;
    private decimal _customQuality = 80;
    private bool _removeMetadata = true;
    private string? _errorMessage;
    private string? _diagnosticId;
    private ImageJobResult? _lastResult;
    private ImageQuality? _appliedQuality;

    public CompressionEditorViewModel(
        IDesktopPickerService picker,
        IDesktopLauncherService launcher,
        IDesktopDialogService dialogs,
        IDesktopClipboardService clipboard,
        ResultOutputGuard outputGuard,
        OpenImageWorkflow openImage,
        CreatePreviewWorkflow createPreview,
        LoadSettingsWorkflow loadSettings,
        CompressImageWorkflow compress,
        DesktopNavigationCoordinator navigation)
    {
        _picker = picker ?? throw new ArgumentNullException(nameof(picker));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _outputGuard = outputGuard ?? throw new ArgumentNullException(nameof(outputGuard));
        _openImage = openImage ?? throw new ArgumentNullException(nameof(openImage));
        _createPreview = createPreview ?? throw new ArgumentNullException(nameof(createPreview));
        _loadSettings = loadSettings ?? throw new ArgumentNullException(nameof(loadSettings));
        _compress = compress ?? throw new ArgumentNullException(nameof(compress));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        Output = new OutputPolicyEditorViewModel(_picker, OutputDraftChanged);

        Modes =
        [
            new DesktopChoiceOption<CompressionMode>("智能", CompressionMode.Smart),
            new DesktopChoiceOption<CompressionMode>("高质量", CompressionMode.HighQuality),
            new DesktopChoiceOption<CompressionMode>("平衡", CompressionMode.Balanced),
            new DesktopChoiceOption<CompressionMode>("极限", CompressionMode.Maximum),
            new DesktopChoiceOption<CompressionMode>("自定义", CompressionMode.Custom)
        ];
        _selectedMode = Modes[0];

        SelectImageCommand = new AsyncCommand(SelectImageAsync, () => !IsProcessing);
        StartCommand = new AsyncCommand(StartAsync, () => CanStart);
        CancelCommand = new RelayCommand<object?>(_ => _executionCancellation?.Cancel(), _ => IsProcessing);
        OpenOutputCommand = new AsyncCommand(OpenOutputAsync, () => IsSuccess);
        ContinueResizeCommand = new RelayCommand<object?>(_ => ContinueResize(), _ => IsSuccess);
        CopyDiagnosticIdCommand = new AsyncCommand(CopyDiagnosticIdAsync, () => HasDiagnosticId);
    }

    public IReadOnlyList<DesktopChoiceOption<CompressionMode>> Modes { get; }
    public OutputPolicyEditorViewModel Output { get; }

    public DesktopContentState ContentState
    {
        get => _contentState;
        private set { if (SetProperty(ref _contentState, value)) NotifyState(); }
    }

    public DesktopExecutionState ExecutionState
    {
        get => _executionState;
        private set { if (SetProperty(ref _executionState, value)) NotifyState(); }
    }

    public DesktopChoiceOption<CompressionMode> SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (value is not null && SetProperty(ref _selectedMode, value))
            {
                MarkDraftChanged();
                NotifyDraft();
            }
        }
    }

    public decimal CustomQuality
    {
        get => _customQuality;
        set
        {
            if (SetProperty(ref _customQuality, value))
            {
                OnPropertyChanged(nameof(CustomQualitySlider));
                MarkDraftChanged();
                NotifyDraft();
            }
        }
    }

    public double CustomQualitySlider
    {
        get => decimal.ToDouble(CustomQuality);
        set => CustomQuality = Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    public bool RemoveMetadata
    {
        get => _removeMetadata;
        set
        {
            if (SetProperty(ref _removeMetadata, value))
            {
                MarkDraftChanged();
                NotifyDraft();
            }
        }
    }

    public bool IsEmpty => ContentState == DesktopContentState.Empty;
    public bool IsContentLoading => ContentState == DesktopContentState.Loading;
    public bool IsContentReady => ContentState == DesktopContentState.Ready;
    public bool IsProcessing => ExecutionState == DesktopExecutionState.Processing;
    public bool IsCustomMode => SelectedMode.Value == CompressionMode.Custom;
    public bool UsesLossyQuality => _probe?.Format is ImageFormatKind.Jpeg or ImageFormatKind.WebP;
    public bool ShowCustomQuality => IsCustomMode && UsesLossyQuality;
    public bool ShowCustomQualityUnavailable => IsCustomMode && !UsesLossyQuality;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasDiagnosticId => !string.IsNullOrWhiteSpace(DiagnosticId);
    public bool HasResult => _lastResult is not null;
    public bool IsSuccess => ExecutionState == DesktopExecutionState.Success;
    public bool IsCanceled => ExecutionState == DesktopExecutionState.Canceled;
    public bool CanStart => IsContentReady && !IsProcessing && TryBuildProfile(out _, out _) && Output.IsValid;
    public string InputName => _inputPath is null ? string.Empty : Path.GetFileName(_inputPath.Value.Value);
    public string InputPath => _inputPath?.Value ?? string.Empty;
    public string InputSummary => _probe is null ? string.Empty : $"{_probe.Width} × {_probe.Height}  ·  {_probe.Format.ToString().ToUpperInvariant()}";
    public byte[]? PreviewBytes { get => _previewBytes; private set => SetProperty(ref _previewBytes, value); }
    public string? DraftError
    {
        get
        {
            if (!TryBuildProfile(out _, out var error)) return error;
            Output.TryBuild(out _, out error);
            return error;
        }
    }
    public bool HasDraftError => !string.IsNullOrWhiteSpace(DraftError);
    public string? ErrorMessage { get => _errorMessage; private set { if (SetProperty(ref _errorMessage, value)) OnPropertyChanged(nameof(HasError)); } }
    public string? DiagnosticId { get => _diagnosticId; private set { if (SetProperty(ref _diagnosticId, value)) { OnPropertyChanged(nameof(HasDiagnosticId)); CopyDiagnosticIdCommand.NotifyCanExecuteChanged(); } } }
    public string ResultTitle => _lastResult?.Status switch
    {
        ImageJobStatus.Succeeded => "压缩完成",
        ImageJobStatus.Canceled => "任务已取消",
        ImageJobStatus.Skipped => "目标已存在，未生成新文件",
        ImageJobStatus.Failed => "压缩失败",
        _ => string.Empty
    };
    public string ResultOutputPath => _lastResult?.OutputPath?.Value ?? string.Empty;
    public bool IsResultOutputAvailable => IsSuccess && _outputGuard.FileExists(_lastResult?.OutputPath);
    public bool IsResultOutputMissing => IsSuccess && _lastResult?.OutputPath is not null && !IsResultOutputAvailable;
    public string ResultSizeChange => DesktopResultText.FormatSizeChange(_lastResult);
    public string AppliedQualityText => _appliedQuality is null ? string.Empty : $"实际质量：{_appliedQuality.Value.Value}";
    public bool HasAppliedQuality => _appliedQuality is not null;

    public AsyncCommand SelectImageCommand { get; }
    public AsyncCommand StartCommand { get; }
    public RelayCommand<object?> CancelCommand { get; }
    public AsyncCommand OpenOutputCommand { get; }
    public RelayCommand<object?> ContinueResizeCommand { get; }
    public AsyncCommand CopyDiagnosticIdCommand { get; }

    public void RequestCancellation() => _executionCancellation?.Cancel();

    public void RefreshResultAvailability() => NotifyResult();

    public async Task LoadAsync(SingleImageNavigationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var initializeDraft = _inputPath is null;
        var generation = BeginLoad(cancellationToken, out var loadCancellation);
        _inputPath = context.InputPath;
        _probe = context.Probe;
        PreviewBytes = null;
        ErrorMessage = null;
        DiagnosticId = null;
        _lastResult = null;
        _appliedQuality = null;
        ExecutionState = DesktopExecutionState.Draft;
        ContentState = DesktopContentState.Loading;
        NotifyInput();
        NotifyResult();

        if (initializeDraft)
        {
            var settings = await _loadSettings.ExecuteAsync(new LoadSettingsRequest(), loadCancellation.Token);
            if (!IsCurrentLoad(generation, loadCancellation)) return;
            if (!settings.Succeeded)
            {
                SetError(settings.Error);
                ContentState = DesktopContentState.Failure;
                return;
            }

            ApplyDefaults(settings.Value!.Settings.DefaultCompressionProfile, settings.Value.Settings.DefaultOutputPolicy);
        }

        ContentState = DesktopContentState.Ready;
        var preview = await _createPreview.ExecuteAsync(new CreatePreviewRequest(context.InputPath, 1600), loadCancellation.Token);
        if (!IsCurrentLoad(generation, loadCancellation)) return;
        if (preview.Succeeded)
        {
            PreviewBytes = preview.Value!.Preview.EncodedBytes;
        }
        else
        {
            SetError(preview.Error);
        }
    }

    public void Clear()
    {
        CancelLoad();
        _inputPath = null;
        _probe = null;
        _lastResult = null;
        _appliedQuality = null;
        PreviewBytes = null;
        ErrorMessage = null;
        DiagnosticId = null;
        ExecutionState = DesktopExecutionState.Draft;
        ContentState = DesktopContentState.Empty;
        NotifyInput();
        NotifyResult();
    }

    public void Dispose()
    {
        CancelLoad();
        _executionCancellation?.Cancel();
        _executionCancellation?.Dispose();
    }

    private async Task SelectImageAsync(CancellationToken cancellationToken)
    {
        var selection = await _picker.PickSingleImageAsync(cancellationToken);
        if (selection.Status == DesktopSelectionStatus.Canceled) return;
        if (selection.Status != DesktopSelectionStatus.Selected || selection.Paths.Count != 1)
        {
            ErrorMessage = DesktopErrorText.FromPicker(selection.ErrorMessage);
            return;
        }

        var path = new LocalPath(selection.Paths[0]);
        var opened = await _openImage.ExecuteAsync(new OpenImageRequest(path), cancellationToken);
        if (!opened.Succeeded) { SetError(opened.Error); return; }
        await LoadAsync(new SingleImageNavigationContext(path, opened.Value!.ProbeResult), cancellationToken);
    }

    private async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_inputPath is null
            || !TryBuildProfile(out var profile, out _)
            || !Output.TryBuild(out var outputPolicy, out _)) return;
        if (!_navigation.TryBeginForegroundTask()) { ErrorMessage = "已有任务正在运行，请等待其结束。"; return; }

        using var executionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _executionCancellation = executionCancellation;
        ErrorMessage = null;
        DiagnosticId = null;
        ExecutionState = DesktopExecutionState.Processing;
        try
        {
            var result = await _compress.ExecuteAsync(
                new CompressImageRequest(_inputPath.Value, profile!, outputPolicy!),
                executionCancellation.Token);
            if (!result.Succeeded)
            {
                if (result.Error?.Code == AtomPixErrorCode.OutputPathConflictsWithInput)
                {
                    await HandleSourceConflictAsync(executionCancellation.Token);
                }
                else
                {
                    SetError(result.Error);
                }
                ExecutionState = result.Error?.Code == AtomPixErrorCode.OperationCanceled ? DesktopExecutionState.Canceled : DesktopExecutionState.Failure;
                return;
            }

            _lastResult = result.Value!.JobResult;
            _appliedQuality = result.Value.AppliedQuality;
            ExecutionState = ToExecutionState(_lastResult.Status);
            if (_lastResult.Status == ImageJobStatus.Failed) SetError(_lastResult.Error);
            NotifyResult();
        }
        finally
        {
            _executionCancellation = null;
            _navigation.EndForegroundTask();
        }
    }

    private async Task HandleSourceConflictAsync(CancellationToken cancellationToken)
    {
        var useAutoRename = await _dialogs.ConfirmAsync(
            "无法覆盖原始图片",
            "当前输出位置和文件名会覆盖源文件。AtomPix 禁止原地覆盖输入图片。",
            "改为自动重命名",
            "返回修改",
            cancellationToken);
        if (useAutoRename)
        {
            Output.SetOverwrite(OverwritePolicy.AutoRename);
            ErrorMessage = "已改为自动重命名，请确认后重新开始。";
        }
        else
        {
            ErrorMessage = DesktopErrorText.FromWorkflow(new AtomPixError(AtomPixErrorCode.OutputPathConflictsWithInput, AtomPixErrorCategory.Validation, "Conflict"));
        }
    }

    private async Task OpenOutputAsync(CancellationToken cancellationToken)
    {
        if (!EnsureResultOutputAvailable()) return;
        var directory = Path.GetDirectoryName(ResultOutputPath);
        if (string.IsNullOrWhiteSpace(directory) || !await _launcher.OpenDirectoryAsync(directory, cancellationToken))
        {
            ErrorMessage = "无法打开输出目录，文件可能已被移动。";
        }
    }

    private void ContinueResize()
    {
        if (!EnsureResultOutputAvailable()) return;
        if (_lastResult?.OutputPath is not { } output) return;
        _ = ContinueResizeAsync(output);
    }

    private bool EnsureResultOutputAvailable()
    {
        if (IsResultOutputAvailable) return true;
        ErrorMessage = "输出文件已不存在，无法继续操作。请重新处理原图片。";
        NotifyResult();
        return false;
    }

    private async Task ContinueResizeAsync(LocalPath output)
    {
        var opened = await _openImage.ExecuteAsync(new OpenImageRequest(output), CancellationToken.None);
        if (!opened.Succeeded) { SetError(opened.Error); return; }
        _navigation.Navigate(new DesktopNavigationRequest(DesktopRoute.Resize, new SingleImageNavigationContext(output, opened.Value!.ProbeResult)));
    }

    private async Task CopyDiagnosticIdAsync(CancellationToken cancellationToken)
    {
        if (DiagnosticId is not null) await _clipboard.SetTextAsync(DiagnosticId, cancellationToken);
    }

    private bool TryBuildProfile(out CompressionProfile? profile, out string? error)
    {
        profile = null;
        error = null;
        if (_probe is null) { error = "请先选择图片。"; return false; }
        if (_probe.IsAnimated || _probe.FrameCount != 1 || _probe.Format is not (ImageFormatKind.Jpeg or ImageFormatKind.Png or ImageFormatKind.WebP))
        {
            error = "当前格式不能直接压缩，请先转换为 JPEG、PNG 或单帧 WebP。";
            return false;
        }

        try
        {
            var mode = SelectedMode.Value;
            ImageQuality? quality = mode switch
            {
                CompressionMode.Smart => null,
                CompressionMode.HighQuality => new ImageQuality(90),
                CompressionMode.Balanced => new ImageQuality(80),
                CompressionMode.Maximum => new ImageQuality(65),
                CompressionMode.Custom => new ImageQuality(ToQuality(CustomQuality)),
                _ => throw new ArgumentOutOfRangeException()
            };
            profile = new CompressionProfile(mode, quality, RemoveMetadata ? MetadataPolicy.Remove : MetadataPolicy.Preserve);
            return true;
        }
        catch (ArgumentException)
        {
            error = "自定义质量必须是 1 到 100 的整数。";
            return false;
        }
    }

    private void ApplyDefaults(CompressionProfile profile, OutputPolicy outputPolicy)
    {
        _selectedMode = Modes.First(item => item.Value == profile.Mode);
        _customQuality = profile.Quality?.Value ?? 80;
        _removeMetadata = profile.MetadataPolicy == MetadataPolicy.Remove;
        Output.Apply(outputPolicy);
        OnPropertyChanged(nameof(SelectedMode));
        OnPropertyChanged(nameof(CustomQuality));
        OnPropertyChanged(nameof(CustomQualitySlider));
        OnPropertyChanged(nameof(RemoveMetadata));
        NotifyDraft();
    }

    private static int ToQuality(decimal value)
    {
        if (value != decimal.Truncate(value) || value is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(value));
        return decimal.ToInt32(value);
    }

    private static DesktopExecutionState ToExecutionState(ImageJobStatus status) => status switch
    {
        ImageJobStatus.Succeeded => DesktopExecutionState.Success,
        ImageJobStatus.Canceled => DesktopExecutionState.Canceled,
        ImageJobStatus.Skipped => DesktopExecutionState.Skipped,
        _ => DesktopExecutionState.Failure
    };

    private void MarkDraftChanged()
    {
        if (ExecutionState is DesktopExecutionState.Success or DesktopExecutionState.Failure or DesktopExecutionState.Canceled or DesktopExecutionState.Skipped)
        {
            ExecutionState = DesktopExecutionState.Draft;
            ErrorMessage = null;
            DiagnosticId = null;
        }
    }

    private void SetError(AtomPixError? error)
    {
        ErrorMessage = DesktopErrorText.FromWorkflow(error);
        DiagnosticId = DesktopErrorText.DiagnosticId(error);
    }

    private int BeginLoad(CancellationToken cancellationToken, out CancellationTokenSource source)
    {
        CancelLoad();
        source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loadCancellation = source;
        return ++_loadGeneration;
    }

    private bool IsCurrentLoad(int generation, CancellationTokenSource source) => generation == _loadGeneration && !source.IsCancellationRequested;

    private void CancelLoad()
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
    }

    private void NotifyInput()
    {
        OnPropertyChanged(nameof(InputName));
        OnPropertyChanged(nameof(InputPath));
        OnPropertyChanged(nameof(InputSummary));
        OnPropertyChanged(nameof(UsesLossyQuality));
        NotifyDraft();
    }

    private void NotifyDraft()
    {
        OnPropertyChanged(nameof(IsCustomMode));
        OnPropertyChanged(nameof(ShowCustomQuality));
        OnPropertyChanged(nameof(ShowCustomQualityUnavailable));
        OnPropertyChanged(nameof(DraftError));
        OnPropertyChanged(nameof(HasDraftError));
        OnPropertyChanged(nameof(CanStart));
        StartCommand.NotifyCanExecuteChanged();
    }

    private void OutputDraftChanged()
    {
        MarkDraftChanged();
        NotifyDraft();
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsContentLoading));
        OnPropertyChanged(nameof(IsContentReady));
        OnPropertyChanged(nameof(IsProcessing));
        OnPropertyChanged(nameof(IsSuccess));
        OnPropertyChanged(nameof(IsCanceled));
        OnPropertyChanged(nameof(CanStart));
        StartCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        SelectImageCommand.NotifyCanExecuteChanged();
        OpenOutputCommand.NotifyCanExecuteChanged();
        ContinueResizeCommand.NotifyCanExecuteChanged();
    }

    private void NotifyResult()
    {
        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(ResultTitle));
        OnPropertyChanged(nameof(ResultOutputPath));
        OnPropertyChanged(nameof(IsResultOutputAvailable));
        OnPropertyChanged(nameof(IsResultOutputMissing));
        OnPropertyChanged(nameof(ResultSizeChange));
        OnPropertyChanged(nameof(AppliedQualityText));
        OnPropertyChanged(nameof(HasAppliedQuality));
        OpenOutputCommand.NotifyCanExecuteChanged();
        ContinueResizeCommand.NotifyCanExecuteChanged();
    }
}
