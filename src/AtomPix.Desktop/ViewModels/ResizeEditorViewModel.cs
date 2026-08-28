namespace AtomPix.Desktop.ViewModels;

using AtomPix.Core.Jobs;
using AtomPix.Core.Errors;
using AtomPix.Core.Output;
using AtomPix.Core.Resize;
using AtomPix.Core.ValueObjects;
using AtomPix.Desktop.Navigation;
using AtomPix.Desktop.Platform;
using AtomPix.Imaging.Abstractions.Processing;
using AtomPix.Workflows.Images;
using AtomPix.Workflows.Settings;

public enum ResizeDraftMode
{
    Pixel,
    Percentage
}

public enum PixelDimensionAnchor
{
    Width,
    Height
}

public sealed class ResizeEditorViewModel : ObservableObject, IDisposable, IToolEditorActions, IResultAvailabilityAware, IOperationResultFeedbackSource
{
    private readonly IDesktopPickerService _picker;
    private readonly IDesktopLauncherService _launcher;
    private readonly IDesktopDialogService _dialogs;
    private readonly IDesktopClipboardService _clipboard;
    private readonly ResultOutputGuard _outputGuard;
    private readonly OpenImageWorkflow _openImage;
    private readonly LoadSettingsWorkflow _loadSettings;
    private readonly ResizeImageWorkflow _resize;
    private readonly IImageProcessor _imageProcessor;
    private readonly DesktopNavigationCoordinator _taskGate;
    private CancellationTokenSource? _loadCancellation;
    private CancellationTokenSource? _executionCancellation;
    private int _loadGeneration;
    private DesktopContentState _contentState = DesktopContentState.Empty;
    private DesktopExecutionState _executionState = DesktopExecutionState.Draft;
    private LocalPath? _inputPath;
    private ImageProbeResult? _probe;
    private DesktopChoiceOption<ResizeDraftMode> _selectedMode;
    private decimal? _pixelWidth;
    private decimal? _pixelHeight;
    private PixelDimensionAnchor _pixelAnchor = PixelDimensionAnchor.Width;
    private bool _maintainAspectRatio = true;
    private bool _preventUpscaling;
    private decimal _percentage = 50;
    private bool _isSynchronizingPixelDimensions;
    private SameFormatEncodingPolicy _encodingPolicy = SameFormatEncodingPolicy.Default;
    private string? _errorMessage;
    private string? _diagnosticId;
    private ImageJobResult? _lastResult;
    private string? _resultDetails;

    public ResizeEditorViewModel(
        IDesktopPickerService picker,
        IDesktopLauncherService launcher,
        IDesktopDialogService dialogs,
        IDesktopClipboardService clipboard,
        ResultOutputGuard outputGuard,
        OpenImageWorkflow openImage,
        LoadSettingsWorkflow loadSettings,
        ResizeImageWorkflow resize,
        IImageProcessor imageProcessor,
        DesktopNavigationCoordinator taskGate)
    {
        _picker = picker ?? throw new ArgumentNullException(nameof(picker));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _outputGuard = outputGuard ?? throw new ArgumentNullException(nameof(outputGuard));
        _openImage = openImage ?? throw new ArgumentNullException(nameof(openImage));
        _loadSettings = loadSettings ?? throw new ArgumentNullException(nameof(loadSettings));
        _resize = resize ?? throw new ArgumentNullException(nameof(resize));
        _imageProcessor = imageProcessor ?? throw new ArgumentNullException(nameof(imageProcessor));
        _taskGate = taskGate ?? throw new ArgumentNullException(nameof(taskGate));
        Output = new OutputPolicyEditorViewModel(_picker, OutputDraftChanged, ResultFeedback.ShowWarning);

        ResizeModes =
        [
            new DesktopChoiceOption<ResizeDraftMode>("按像素", ResizeDraftMode.Pixel),
            new DesktopChoiceOption<ResizeDraftMode>("按百分比", ResizeDraftMode.Percentage)
        ];
        _selectedMode = ResizeModes[0];

        SelectImageCommand = new AsyncCommand(SelectImageAsync, () => !IsProcessing);
        StartCommand = new AsyncCommand(StartAsync, () => IsContentReady && !IsProcessing);
        CancelCommand = new RelayCommand<object?>(_ => _executionCancellation?.Cancel(), _ => IsProcessing);
        OpenOutputCommand = new AsyncCommand(OpenOutputAsync, () => IsSuccess);
        CopyDiagnosticIdCommand = new AsyncCommand(CopyDiagnosticIdAsync, () => HasDiagnosticId);
    }

    public IReadOnlyList<DesktopChoiceOption<ResizeDraftMode>> ResizeModes { get; }
    public OutputPolicyEditorViewModel Output { get; }
    public OperationResultFeedback ResultFeedback { get; } = new();

    public DesktopContentState ContentState
    {
        get => _contentState;
        private set
        {
            if (SetProperty(ref _contentState, value))
            {
                NotifyState();
            }
        }
    }

    public DesktopExecutionState ExecutionState
    {
        get => _executionState;
        private set
        {
            if (SetProperty(ref _executionState, value))
            {
                NotifyState();
            }
        }
    }

    public DesktopChoiceOption<ResizeDraftMode> SelectedMode
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

    public decimal? PixelWidth
    {
        get => _pixelWidth;
        set
        {
            var anchorChanged = !_isSynchronizingPixelDimensions && _pixelAnchor != PixelDimensionAnchor.Width;
            if (anchorChanged)
            {
                _pixelAnchor = PixelDimensionAnchor.Width;
                OnPropertyChanged(nameof(PixelAnchor));
            }

            if (SetProperty(ref _pixelWidth, value) || anchorChanged)
            {
                SynchronizeHeightFromWidth();
                MarkDraftChanged();
                NotifyDraft();
            }
        }
    }

    public decimal? PixelHeight
    {
        get => _pixelHeight;
        set
        {
            var anchorChanged = !_isSynchronizingPixelDimensions && _pixelAnchor != PixelDimensionAnchor.Height;
            if (anchorChanged)
            {
                _pixelAnchor = PixelDimensionAnchor.Height;
                OnPropertyChanged(nameof(PixelAnchor));
            }

            if (SetProperty(ref _pixelHeight, value) || anchorChanged)
            {
                SynchronizeWidthFromHeight();
                MarkDraftChanged();
                NotifyDraft();
            }
        }
    }

    public bool MaintainAspectRatio
    {
        get => _maintainAspectRatio;
        set
        {
            if (SetProperty(ref _maintainAspectRatio, value))
            {
                if (value)
                {
                    _pixelAnchor = PixelDimensionAnchor.Width;
                    OnPropertyChanged(nameof(PixelAnchor));
                    SynchronizeHeightFromWidth();
                }
                MarkDraftChanged();
                NotifyDraft();
            }
        }
    }

    public bool PreventUpscaling
    {
        get => _preventUpscaling;
        set
        {
            if (SetProperty(ref _preventUpscaling, value))
            {
                MarkDraftChanged();
                NotifyDraft();
            }
        }
    }

    public PixelDimensionAnchor PixelAnchor => _pixelAnchor;

    public decimal Percentage
    {
        get => _percentage;
        set
        {
            if (SetProperty(ref _percentage, value))
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

    public string StartActionLabel => "单张处理";

    System.Windows.Input.ICommand IToolEditorActions.StartActionCommand => StartCommand;

    System.Windows.Input.ICommand IToolEditorActions.CancelActionCommand => CancelCommand;

    public bool IsPixelMode => SelectedMode.Value == ResizeDraftMode.Pixel;

    public bool IsPercentageMode => SelectedMode.Value == ResizeDraftMode.Percentage;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool HasDiagnosticId => !string.IsNullOrWhiteSpace(DiagnosticId);

    public bool HasResult => _lastResult is not null;

    public bool IsSuccess => ExecutionState == DesktopExecutionState.Success;

    public bool IsCanceled => ExecutionState == DesktopExecutionState.Canceled;

    public bool IsSkipped => ExecutionState == DesktopExecutionState.Skipped;

    public bool CanStart =>
        ContentState == DesktopContentState.Ready
        && !IsProcessing
        && TryBuildPolicy(out _, out _)
        && Output.IsValid;

    public string InputPath => _inputPath?.Value ?? string.Empty;

    public string InputName => _inputPath is null ? string.Empty : Path.GetFileName(_inputPath.Value.Value);

    public string InputSummary => _probe is null
        ? string.Empty
        : $"{_probe.Width} × {_probe.Height}  ·  {_probe.Format.ToString().ToUpperInvariant()}";

    public string EstimatedSize
    {
        get
        {
            return TryBuildPolicy(out _, out var resolved)
                ? $"{resolved!.Width} × {resolved.Height} px"
                : "—";
        }
    }

    public string? DraftError
    {
        get
        {
            TryBuildPolicy(out _, out _, out var error);
            return error;
        }
    }

    public bool HasDraftError => !string.IsNullOrWhiteSpace(DraftError);

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

    public string ResultTitle => _lastResult?.Status switch
    {
        ImageJobStatus.Succeeded => "调整尺寸完成",
        ImageJobStatus.Canceled => "任务已取消",
        ImageJobStatus.Skipped => "未生成新文件",
        ImageJobStatus.Failed => "调整尺寸失败",
        _ => string.Empty
    };

    public string ResultOutputPath => _lastResult?.OutputPath?.Value ?? string.Empty;
    public bool IsResultOutputAvailable => IsSuccess && _outputGuard.FileExists(_lastResult?.OutputPath);
    public bool IsResultOutputMissing => IsSuccess && _lastResult?.OutputPath is not null && !IsResultOutputAvailable;

    public string ResultSizeChange => DesktopResultText.FormatSizeChange(_lastResult);

    public string EncodingSummary => $"保留原格式 · 有损质量 {_encodingPolicy.LossyQuality.Value} · {(_encodingPolicy.MetadataPolicy == AtomPix.Core.Compression.MetadataPolicy.Remove ? "移除拍摄信息与位置数据" : "保留拍摄信息与位置数据")} · ICC 保留";
    public SameFormatEncodingPolicy EncodingPolicy => _encodingPolicy;

    public string? DiagnosticId
    {
        get => _diagnosticId;
        private set
        {
            if (SetProperty(ref _diagnosticId, value))
            {
                OnPropertyChanged(nameof(HasDiagnosticId));
                CopyDiagnosticIdCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string? ResultDetails
    {
        get => _resultDetails;
        private set => SetProperty(ref _resultDetails, value);
    }

    public AsyncCommand SelectImageCommand { get; }

    public AsyncCommand StartCommand { get; }

    public RelayCommand<object?> CancelCommand { get; }

    public AsyncCommand OpenOutputCommand { get; }

    public AsyncCommand CopyDiagnosticIdCommand { get; }

    public void RequestCancellation() => _executionCancellation?.Cancel();

    public void RefreshResultAvailability() => NotifyResult();

    public async Task LoadAsync(SingleImageNavigationContext context, CancellationToken cancellationToken = default)
    {
        await LoadCoreAsync(context, preserveDraft: false, cancellationToken);
    }

    public async Task SynchronizeInputAsync(SingleImageNavigationContext context, CancellationToken cancellationToken = default)
    {
        await LoadCoreAsync(context, preserveDraft: true, cancellationToken);
    }

    private async Task LoadCoreAsync(
        SingleImageNavigationContext context,
        bool preserveDraft,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var initializeDraft = !preserveDraft;
        CancelLoad();
        var generation = ++_loadGeneration;
        var loadCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loadCancellation = loadCancellation;

        _inputPath = context.InputPath;
        _probe = context.Probe;
        if (!preserveDraft)
        {
            SetPixelDimensions(context.Probe.Width, context.Probe.Height, PixelDimensionAnchor.Width);
            Percentage = 50;
        }
        else if (MaintainAspectRatio)
        {
            if (PixelAnchor == PixelDimensionAnchor.Width)
            {
                SynchronizeHeightFromWidth();
            }
            else
            {
                SynchronizeWidthFromHeight();
            }
        }
        _lastResult = null;
        ResultFeedback.Dismiss();
        ResultDetails = null;
        ErrorMessage = null;
        DiagnosticId = null;
        ExecutionState = DesktopExecutionState.Draft;
        ContentState = DesktopContentState.Loading;
        NotifyInput();
        NotifyResult();

        if (initializeDraft)
        {
            var settings = await _loadSettings.ExecuteAsync(new LoadSettingsRequest(), loadCancellation.Token);
            if (generation != _loadGeneration || loadCancellation.IsCancellationRequested) return;
            if (!settings.Succeeded)
            {
                SetError(settings.Error);
                ContentState = DesktopContentState.Failure;
                return;
            }

            Output.Apply(settings.Value!.Settings.DefaultOutputPolicy);
            _encodingPolicy = settings.Value.Settings.DefaultSameFormatEncodingPolicy;
            OnPropertyChanged(nameof(EncodingSummary));
        }

        ContentState = DesktopContentState.Ready;
    }

    public void Clear()
    {
        CancelLoad();
        _inputPath = null;
        _probe = null;
        _lastResult = null;
        ResultFeedback.Dismiss();
        ErrorMessage = null;
        DiagnosticId = null;
        ResultDetails = null;
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
        if (selection.Status == DesktopSelectionStatus.Canceled)
        {
            return;
        }

        if (selection.Status != DesktopSelectionStatus.Selected || selection.Paths.Count != 1)
        {
            ErrorMessage = DesktopErrorText.FromPicker(selection.ErrorMessage);
            return;
        }

        var path = new LocalPath(selection.Paths[0]);
        var opened = await _openImage.ExecuteAsync(new OpenImageRequest(path), cancellationToken);
        if (!opened.Succeeded)
        {
            SetError(opened.Error);
            return;
        }

        await LoadAsync(new SingleImageNavigationContext(path, opened.Value!.ProbeResult), cancellationToken);
    }

    private async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_inputPath is null) return;
        if (!TryBuildPolicy(out var policy, out _, out var policyError))
        {
            ResultFeedback.ShowWarning(policyError ?? "当前尺寸配置无效。");
            return;
        }
        if (!Output.TryBuild(out var outputPolicy, out var outputError))
        {
            ResultFeedback.ShowWarning(outputError ?? "当前输出配置无效。");
            return;
        }

        if (!_taskGate.TryBeginForegroundTask())
        {
            ErrorMessage = "已有任务正在运行，请等待其结束。";
            return;
        }

        using var executionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _executionCancellation = executionCancellation;
        ErrorMessage = null;
        DiagnosticId = null;
        ResultFeedback.Dismiss();
        ExecutionState = DesktopExecutionState.Processing;
        try
        {
            var result = await _resize.ExecuteAsync(
                new ResizeImageRequest(_inputPath.Value, policy!, outputPolicy!, _encodingPolicy),
                executionCancellation.Token);
            if (!result.Succeeded)
            {
                if (result.Error?.Code == AtomPixErrorCode.OutputPathConflictsWithInput)
                    await HandleSourceConflictAsync(executionCancellation.Token);
                else
                    SetError(result.Error);
                ExecutionState = result.Error?.Code == AtomPix.Core.Errors.AtomPixErrorCode.OperationCanceled
                    ? DesktopExecutionState.Canceled
                    : DesktopExecutionState.Failure;
                if (ExecutionState == DesktopExecutionState.Canceled) ResultFeedback.ShowCanceled();
                return;
            }

            var value = result.Value!;
            _lastResult = value.JobResult;
            ResultDetails = $"{value.InputSize.Width} × {value.InputSize.Height} → {value.TargetSize.Width} × {value.TargetSize.Height}";
            ExecutionState = value.JobResult.Status switch
            {
                ImageJobStatus.Succeeded => DesktopExecutionState.Success,
                ImageJobStatus.Canceled => DesktopExecutionState.Canceled,
                ImageJobStatus.Skipped => DesktopExecutionState.Skipped,
                _ => DesktopExecutionState.Failure
            };
            if (value.JobResult.Status == ImageJobStatus.Failed)
            {
                SetError(value.JobResult.Error);
            }
            else
            {
                ResultFeedback.Show(value.JobResult, value.OutputDisposition, "调整尺寸完成", ResultDetails);
            }

            NotifyResult();
        }
        finally
        {
            _executionCancellation = null;
            _taskGate.EndForegroundTask();
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
            SetError(new AtomPixError(AtomPixErrorCode.OutputPathConflictsWithInput, AtomPixErrorCategory.Validation, "Conflict"));
        }
    }

    private async Task OpenOutputAsync(CancellationToken cancellationToken)
    {
        if (!EnsureResultOutputAvailable()) return;
        var directory = Path.GetDirectoryName(ResultOutputPath);
        if (string.IsNullOrWhiteSpace(directory) || !await _launcher.OpenDirectoryAsync(directory, cancellationToken))
            ErrorMessage = "无法打开输出目录，文件可能已被移动。";
    }

    private bool EnsureResultOutputAvailable()
    {
        if (IsResultOutputAvailable) return true;
        ErrorMessage = "输出文件已不存在，无法继续操作。请重新处理原图片。";
        NotifyResult();
        return false;
    }

    private async Task CopyDiagnosticIdAsync(CancellationToken cancellationToken)
    {
        if (DiagnosticId is not null) await _clipboard.SetTextAsync(DiagnosticId, cancellationToken);
    }

    private bool TryBuildPolicy(out ResizePolicy? policy, out ResolvedResizeSize? resolved) =>
        TryBuildPolicy(out policy, out resolved, out _);

    private bool TryBuildPolicy(
        out ResizePolicy? policy,
        out ResolvedResizeSize? resolved,
        out string? error)
    {
        policy = null;
        resolved = null;
        error = null;
        if (_probe is null)
        {
            error = "请先选择图片。";
            return false;
        }

        var capability = _imageProcessor.Capabilities.Resize;
        if (_probe.IsAnimated || _probe.FrameCount != 1 || capability is null || !capability.SupportedSameFormatFormats.Contains(_probe.Format))
        {
            error = "当前格式不能直接调整尺寸，请先转换为 JPEG、PNG、BMP 或单帧 WebP。";
            return false;
        }

        try
        {
            policy = SelectedMode.Value switch
            {
                ResizeDraftMode.Pixel => new PixelResizePolicy(
                    MaintainAspectRatio && PixelAnchor == PixelDimensionAnchor.Height ? null : ToPositiveInteger(PixelWidth),
                    MaintainAspectRatio && PixelAnchor == PixelDimensionAnchor.Width ? null : ToPositiveInteger(PixelHeight),
                    MaintainAspectRatio,
                    PreventUpscaling),
                ResizeDraftMode.Percentage => new PercentageResizePolicy(Percentage),
                _ => throw new ArgumentOutOfRangeException()
            };
            resolved = policy.Resolve(new ImageSize(_probe.Width, _probe.Height));
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            error = SelectedMode.Value == ResizeDraftMode.Pixel
                ? "请输入有效的整数宽度和高度；保持比例时至少填写一项。"
                : "请输入大于 0 的百分比。";
            return false;
        }

        var pixels = checked((long)resolved.Width * resolved.Height);
        if (resolved.Width > capability.MaxWidth || resolved.Height > capability.MaxHeight || pixels > capability.MaxPixelCount)
        {
            error = "预计输出尺寸超过当前处理上限。";
            return false;
        }

        return true;
    }

    private static int? ToPositiveInteger(decimal? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value <= 0 || value > int.MaxValue || value != decimal.Truncate(value.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        return decimal.ToInt32(value.Value);
    }

    private void MarkDraftChanged()
    {
        ResultFeedback.Dismiss();
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

    private void NotifyDraft()
    {
        OnPropertyChanged(nameof(IsPixelMode));
        OnPropertyChanged(nameof(IsPercentageMode));
        OnPropertyChanged(nameof(EstimatedSize));
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

    private void NotifyInput()
    {
        OnPropertyChanged(nameof(InputPath));
        OnPropertyChanged(nameof(InputName));
        OnPropertyChanged(nameof(InputSummary));
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
        OnPropertyChanged(nameof(IsSkipped));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(ResultTitle));
        StartCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        SelectImageCommand.NotifyCanExecuteChanged();
    }

    private void SynchronizeHeightFromWidth()
    {
        if (_isSynchronizingPixelDimensions || !MaintainAspectRatio || _probe is null
            || !TryResolveLinkedSize(PixelDimensionAnchor.Width, out var resolved))
        {
            return;
        }

        _isSynchronizingPixelDimensions = true;
        try
        {
            SetProperty(ref _pixelHeight, (decimal)resolved.Height, nameof(PixelHeight));
        }
        finally
        {
            _isSynchronizingPixelDimensions = false;
        }
    }

    private void SynchronizeWidthFromHeight()
    {
        if (_isSynchronizingPixelDimensions || !MaintainAspectRatio || _probe is null
            || !TryResolveLinkedSize(PixelDimensionAnchor.Height, out var resolved))
        {
            return;
        }

        _isSynchronizingPixelDimensions = true;
        try
        {
            SetProperty(ref _pixelWidth, (decimal)resolved.Width, nameof(PixelWidth));
        }
        finally
        {
            _isSynchronizingPixelDimensions = false;
        }
    }

    private bool TryResolveLinkedSize(PixelDimensionAnchor anchor, out ResolvedResizeSize resolved)
    {
        resolved = default!;
        if (_probe is null)
        {
            return false;
        }

        try
        {
            var width = anchor == PixelDimensionAnchor.Width ? ToPositiveInteger(PixelWidth) : null;
            var height = anchor == PixelDimensionAnchor.Height ? ToPositiveInteger(PixelHeight) : null;
            resolved = new PixelResizePolicy(width, height, maintainAspectRatio: true)
                .Resolve(new ImageSize(_probe.Width, _probe.Height));
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            return false;
        }
    }

    public void ApplyResizeDraft(
        ResizeDraftMode mode,
        decimal? width,
        decimal? height,
        PixelDimensionAnchor anchor,
        bool maintainAspectRatio,
        bool preventUpscaling,
        decimal percentage)
    {
        _isSynchronizingPixelDimensions = true;
        try
        {
            SetProperty(ref _selectedMode, ResizeModes.First(option => option.Value == mode), nameof(SelectedMode));
            SetProperty(ref _pixelWidth, width, nameof(PixelWidth));
            SetProperty(ref _pixelHeight, height, nameof(PixelHeight));
            SetProperty(ref _maintainAspectRatio, maintainAspectRatio, nameof(MaintainAspectRatio));
            SetProperty(ref _preventUpscaling, preventUpscaling, nameof(PreventUpscaling));
            SetProperty(ref _percentage, percentage, nameof(Percentage));
            _pixelAnchor = anchor;
            OnPropertyChanged(nameof(PixelAnchor));
        }
        finally
        {
            _isSynchronizingPixelDimensions = false;
        }

        MarkDraftChanged();
        NotifyDraft();
    }

    private void SetPixelDimensions(decimal? width, decimal? height, PixelDimensionAnchor anchor)
    {
        _isSynchronizingPixelDimensions = true;
        try
        {
            SetProperty(ref _pixelWidth, width, nameof(PixelWidth));
            SetProperty(ref _pixelHeight, height, nameof(PixelHeight));
            _pixelAnchor = anchor;
            OnPropertyChanged(nameof(PixelAnchor));
        }
        finally
        {
            _isSynchronizingPixelDimensions = false;
        }
    }

    private void NotifyResult()
    {
        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(ResultTitle));
        OnPropertyChanged(nameof(ResultOutputPath));
        OnPropertyChanged(nameof(IsResultOutputAvailable));
        OnPropertyChanged(nameof(IsResultOutputMissing));
        OnPropertyChanged(nameof(ResultSizeChange));
        OpenOutputCommand.NotifyCanExecuteChanged();
    }

    private void CancelLoad()
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
    }
}
