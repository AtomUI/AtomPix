namespace AtomPix.Desktop.ViewModels;

using AtomPix.Core.Crop;
using AtomPix.Core.Errors;
using AtomPix.Core.Jobs;
using AtomPix.Core.Output;
using AtomPix.Core.Resize;
using AtomPix.Core.ValueObjects;
using AtomPix.Desktop.Controls;
using AtomPix.Desktop.Navigation;
using AtomPix.Desktop.Platform;
using AtomPix.Imaging.Abstractions.Formats;
using AtomPix.Imaging.Abstractions.Processing;
using AtomPix.Workflows.Images;
using AtomPix.Workflows.Settings;

public sealed record CropRatioOption(string Label, int? WidthUnits, int? HeightUnits)
{
    public double Ratio => WidthUnits is null || HeightUnits is null ? 0 : WidthUnits.Value / (double)HeightUnits.Value;
    public override string ToString() => Label;
}

public sealed class CropEditorViewModel : ObservableObject, IDisposable, IDesktopForegroundTask, IResultAvailabilityAware, IOperationResultFeedbackSource
{
    private readonly IDesktopPickerService _picker;
    private readonly IDesktopLauncherService _launcher;
    private readonly IDesktopDialogService _dialogs;
    private readonly IDesktopClipboardService _clipboard;
    private readonly ResultOutputGuard _outputGuard;
    private readonly OpenImageWorkflow _openImage;
    private readonly LoadSettingsWorkflow _loadSettings;
    private readonly CropImageWorkflow _crop;
    private readonly DesktopNavigationCoordinator _navigation;
    private CancellationTokenSource? _loadCancellation;
    private CancellationTokenSource? _executionCancellation;
    private int _loadGeneration;
    private DesktopContentState _contentState = DesktopContentState.Empty;
    private DesktopExecutionState _executionState = DesktopExecutionState.Draft;
    private LocalPath? _inputPath;
    private ImageProbeResult? _probe;
    private CropRatioOption _selectedRatio;
    private decimal _cropX;
    private decimal _cropY;
    private decimal _cropWidth = 1;
    private decimal _cropHeight = 1;
    private bool _synchronizingSelection;
    private SameFormatEncodingPolicy _encodingPolicy = SameFormatEncodingPolicy.Default;
    private string? _errorMessage;
    private string? _diagnosticId;
    private ImageJobResult? _lastResult;
    private ImageSize? _actualOutputSize;

    public CropEditorViewModel(
        IDesktopPickerService picker,
        IDesktopLauncherService launcher,
        IDesktopDialogService dialogs,
        IDesktopClipboardService clipboard,
        ResultOutputGuard outputGuard,
        OpenImageWorkflow openImage,
        LoadSettingsWorkflow loadSettings,
        CropImageWorkflow crop,
        DesktopNavigationCoordinator navigation)
    {
        _picker = picker ?? throw new ArgumentNullException(nameof(picker));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _outputGuard = outputGuard ?? throw new ArgumentNullException(nameof(outputGuard));
        _openImage = openImage ?? throw new ArgumentNullException(nameof(openImage));
        _loadSettings = loadSettings ?? throw new ArgumentNullException(nameof(loadSettings));
        _crop = crop ?? throw new ArgumentNullException(nameof(crop));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        Output = new OutputPolicyEditorViewModel(_picker, OutputDraftChanged, ResultFeedback.ShowWarning);

        Ratios =
        [
            new CropRatioOption("自定义", null, null),
            new CropRatioOption("3:2", 3, 2),
            new CropRatioOption("4:3", 4, 3),
            new CropRatioOption("5:4", 5, 4),
            new CropRatioOption("1:1", 1, 1)
        ];
        _selectedRatio = Ratios[0];
        SelectImageCommand = new AsyncCommand(SelectImageAsync, () => !IsProcessing);
        StartCommand = new AsyncCommand(StartAsync, () => IsContentReady && !IsProcessing);
        CancelCommand = new RelayCommand<object?>(_ => _executionCancellation?.Cancel(), _ => IsProcessing);
        OpenOutputCommand = new AsyncCommand(OpenOutputAsync, () => IsSuccess);
        ContinueResizeCommand = new RelayCommand<object?>(_ => ContinueResize(), _ => IsSuccess);
        CopyDiagnosticIdCommand = new AsyncCommand(CopyDiagnosticIdAsync, () => HasDiagnosticId);
    }

    public IReadOnlyList<CropRatioOption> Ratios { get; }
    public OutputPolicyEditorViewModel Output { get; }
    public OperationResultFeedback ResultFeedback { get; } = new();
    public DesktopContentState ContentState { get => _contentState; private set { if (SetProperty(ref _contentState, value)) NotifyState(); } }
    public DesktopExecutionState ExecutionState { get => _executionState; private set { if (SetProperty(ref _executionState, value)) NotifyState(); } }
    public CropRatioOption SelectedRatio
    {
        get => _selectedRatio;
        set
        {
            if (value is not null && SetProperty(ref _selectedRatio, value))
            {
                ApplyRatioPreset(value);
                MarkDraftChanged();
                NotifySelection();
            }
        }
    }

    public decimal CropX { get => _cropX; set { if (SetProperty(ref _cropX, value)) { MarkDraftChanged(); NotifySelection(); } } }
    public decimal CropY { get => _cropY; set { if (SetProperty(ref _cropY, value)) { MarkDraftChanged(); NotifySelection(); } } }
    public decimal CropWidth
    {
        get => _cropWidth;
        set
        {
            if (SetProperty(ref _cropWidth, value))
            {
                if (!_synchronizingSelection && SelectedRatio.Ratio > 0 && value > 0)
                {
                    _synchronizingSelection = true;
                    CropHeight = Math.Max(1, decimal.Floor(value / (decimal)SelectedRatio.Ratio));
                    _synchronizingSelection = false;
                }
                MarkDraftChanged();
                NotifySelection();
            }
        }
    }
    public decimal CropHeight
    {
        get => _cropHeight;
        set
        {
            if (SetProperty(ref _cropHeight, value))
            {
                if (!_synchronizingSelection && SelectedRatio.Ratio > 0 && value > 0)
                {
                    _synchronizingSelection = true;
                    CropWidth = Math.Max(1, decimal.Floor(value * (decimal)SelectedRatio.Ratio));
                    _synchronizingSelection = false;
                }
                MarkDraftChanged();
                NotifySelection();
            }
        }
    }

    public int CanvasX => ToCanvasInteger(CropX);
    public int CanvasY => ToCanvasInteger(CropY);
    public int CanvasWidth => Math.Max(1, ToCanvasInteger(CropWidth));
    public int CanvasHeight => Math.Max(1, ToCanvasInteger(CropHeight));
    public int ImagePixelWidth => _probe?.Width ?? 0;
    public int ImagePixelHeight => _probe?.Height ?? 0;
    public double LockedAspectRatio => SelectedRatio.Ratio;
    public bool IsCustomRatio => SelectedRatio.Ratio <= 0;
    public bool IsEmpty => ContentState == DesktopContentState.Empty;
    public bool IsContentLoading => ContentState == DesktopContentState.Loading;
    public bool IsContentReady => ContentState == DesktopContentState.Ready;
    public bool IsProcessing => ExecutionState == DesktopExecutionState.Processing;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasDiagnosticId => !string.IsNullOrWhiteSpace(DiagnosticId);
    public bool HasResult => _lastResult is not null;
    public bool IsSuccess => ExecutionState == DesktopExecutionState.Success;
    public bool CanStart => IsContentReady && !IsProcessing && TryBuildCropRectangle(out _, out _) && Output.IsValid;
    public string InputName => _inputPath is null ? string.Empty : Path.GetFileName(_inputPath.Value.Value);
    public string InputPath => _inputPath?.Value ?? string.Empty;
    public string InputSummary => _probe is null ? string.Empty : $"{_probe.Width} × {_probe.Height}  ·  {_probe.Format.ToString().ToUpperInvariant()}";
    public string EstimatedOutput => TryBuildCropRectangle(out var area, out _) ? $"{area!.Width} × {area.Height} px" : "—";
    public string? DraftError
    {
        get
        {
            TryBuildCropRectangle(out _, out var error);
            return error;
        }
    }
    public bool HasDraftError => !string.IsNullOrWhiteSpace(DraftError);
    public string EncodingSummary => $"保留原格式 · 有损质量 {_encodingPolicy.LossyQuality.Value} · {(_encodingPolicy.MetadataPolicy == AtomPix.Core.Compression.MetadataPolicy.Remove ? "移除拍摄信息" : "保留拍摄信息")} · ICC 保留";
    public SameFormatEncodingPolicy EncodingPolicy => _encodingPolicy;
    public string? ErrorMessage { get => _errorMessage; private set { if (SetProperty(ref _errorMessage, value)) OnPropertyChanged(nameof(HasError)); } }
    public string? DiagnosticId { get => _diagnosticId; private set { if (SetProperty(ref _diagnosticId, value)) { OnPropertyChanged(nameof(HasDiagnosticId)); CopyDiagnosticIdCommand.NotifyCanExecuteChanged(); } } }
    public string ResultTitle => _lastResult?.Status switch
    {
        ImageJobStatus.Succeeded => "裁剪完成",
        ImageJobStatus.Canceled => "任务已取消",
        ImageJobStatus.Skipped => "目标已存在，未生成新文件",
        ImageJobStatus.Failed => "裁剪失败",
        _ => string.Empty
    };
    public string ResultOutputPath => _lastResult?.OutputPath?.Value ?? string.Empty;
    public bool IsResultOutputAvailable => IsSuccess && _outputGuard.FileExists(_lastResult?.OutputPath);
    public bool IsResultOutputMissing => IsSuccess && _lastResult?.OutputPath is not null && !IsResultOutputAvailable;
    public string ResultDetails => _actualOutputSize is null ? string.Empty : $"实际输出：{_actualOutputSize.Value.Width} × {_actualOutputSize.Value.Height} px";
    public string ResultSizeChange => DesktopResultText.FormatSizeChange(_lastResult);

    public AsyncCommand SelectImageCommand { get; }
    public AsyncCommand StartCommand { get; }
    public RelayCommand<object?> CancelCommand { get; }
    public AsyncCommand OpenOutputCommand { get; }
    public RelayCommand<object?> ContinueResizeCommand { get; }
    public AsyncCommand CopyDiagnosticIdCommand { get; }

    public void RequestCancellation() => _executionCancellation?.Cancel();

    public void RefreshResultAvailability() => NotifyResult();

    public Task LoadAsync(SingleImageNavigationContext context, CancellationToken cancellationToken = default) =>
        LoadCoreAsync(context, initializeDraft: true, cancellationToken);

    public Task SynchronizeInputAsync(SingleImageNavigationContext context, CancellationToken cancellationToken = default) =>
        LoadCoreAsync(context, initializeDraft: false, cancellationToken);

    private async Task LoadCoreAsync(
        SingleImageNavigationContext context,
        bool initializeDraft,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var generation = BeginLoad(cancellationToken, out var loadCancellation);
        _inputPath = context.InputPath;
        _probe = context.Probe;
        ErrorMessage = null;
        DiagnosticId = null;
        _lastResult = null;
        ResultFeedback.Dismiss();
        _actualOutputSize = null;
        ExecutionState = DesktopExecutionState.Draft;
        ContentState = DesktopContentState.Loading;
        ResetSelectionCore();
        NotifyInput();
        NotifyResult();

        if (initializeDraft)
        {
            var settings = await _loadSettings.ExecuteAsync(new LoadSettingsRequest(), loadCancellation.Token);
            if (!IsCurrentLoad(generation, loadCancellation)) return;
            if (!settings.Succeeded) { SetError(settings.Error); ContentState = DesktopContentState.Failure; return; }
            Output.Apply(settings.Value!.Settings.DefaultOutputPolicy);
            _encodingPolicy = settings.Value.Settings.DefaultSameFormatEncodingPolicy;
            OnPropertyChanged(nameof(EncodingSummary));
        }

        ContentState = DesktopContentState.Ready;
    }

    public void ApplyCanvasSelection(CropCanvasSelection selection)
    {
        if (IsProcessing) return;
        _synchronizingSelection = true;
        _cropX = selection.X;
        _cropY = selection.Y;
        _cropWidth = selection.Width;
        _cropHeight = selection.Height;
        _synchronizingSelection = false;
        OnPropertyChanged(nameof(CropX));
        OnPropertyChanged(nameof(CropY));
        OnPropertyChanged(nameof(CropWidth));
        OnPropertyChanged(nameof(CropHeight));
        MarkDraftChanged();
        NotifySelection();
    }

    public void Clear()
    {
        CancelLoad();
        _inputPath = null;
        _probe = null;
        _lastResult = null;
        ResultFeedback.Dismiss();
        _actualOutputSize = null;
        ErrorMessage = null;
        DiagnosticId = null;
        ExecutionState = DesktopExecutionState.Draft;
        ContentState = DesktopContentState.Empty;
        NotifyInput();
        NotifyResult();
    }

    public void Dispose() { CancelLoad(); _executionCancellation?.Cancel(); _executionCancellation?.Dispose(); }

    private async Task SelectImageAsync(CancellationToken cancellationToken)
    {
        var selection = await _picker.PickSingleImageAsync(cancellationToken);
        if (selection.Status == DesktopSelectionStatus.Canceled) return;
        if (selection.Status != DesktopSelectionStatus.Selected || selection.Paths.Count != 1) { ErrorMessage = DesktopErrorText.FromPicker(selection.ErrorMessage); return; }
        var path = new LocalPath(selection.Paths[0]);
        var opened = await _openImage.ExecuteAsync(new OpenImageRequest(path), cancellationToken);
        if (!opened.Succeeded) { SetError(opened.Error); return; }
        await LoadAsync(new SingleImageNavigationContext(path, opened.Value!.ProbeResult), cancellationToken);
    }

    private async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_inputPath is null) return;
        if (!TryBuildCropRectangle(out var area, out var cropError))
        {
            ResultFeedback.ShowWarning(cropError ?? "当前剪裁区域无效。");
            return;
        }
        if (!Output.TryBuild(out var outputPolicy, out var outputError))
        {
            ResultFeedback.ShowWarning(outputError ?? "当前输出配置无效。");
            return;
        }
        if (!_navigation.TryBeginForegroundTask()) { ErrorMessage = "已有任务正在运行，请等待其结束。"; return; }
        using var executionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _executionCancellation = executionCancellation;
        ErrorMessage = null;
        DiagnosticId = null;
        ResultFeedback.Dismiss();
        ExecutionState = DesktopExecutionState.Processing;
        try
        {
            var result = await _crop.ExecuteAsync(new CropImageRequest(_inputPath.Value, area!, outputPolicy!, _encodingPolicy), executionCancellation.Token);
            if (!result.Succeeded)
            {
                if (result.Error?.Code == AtomPixErrorCode.OutputPathConflictsWithInput) await HandleSourceConflictAsync(executionCancellation.Token);
                else SetError(result.Error);
                ExecutionState = result.Error?.Code == AtomPixErrorCode.OperationCanceled ? DesktopExecutionState.Canceled : DesktopExecutionState.Failure;
                if (ExecutionState == DesktopExecutionState.Canceled) ResultFeedback.ShowCanceled();
                return;
            }
            _lastResult = result.Value!.JobResult;
            _actualOutputSize = result.Value.ActualOutputSize;
            ExecutionState = ToExecutionState(_lastResult.Status);
            if (_lastResult.Status == ImageJobStatus.Failed) SetError(_lastResult.Error);
            else ResultFeedback.Show(_lastResult, result.Value.OutputDisposition, "裁剪完成", ResultDetails);
            NotifyResult();
        }
        finally { _executionCancellation = null; _navigation.EndForegroundTask(); }
    }

    private async Task HandleSourceConflictAsync(CancellationToken cancellationToken)
    {
        var useAutoRename = await _dialogs.ConfirmAsync("无法覆盖原始图片", "当前输出位置和文件名会覆盖源文件。AtomPix 禁止原地覆盖输入图片。", "改为自动重命名", "返回修改", cancellationToken);
        if (useAutoRename)
        {
            Output.SetOverwrite(OverwritePolicy.AutoRename);
            ErrorMessage = "已改为自动重命名，请确认后重新开始。";
        }
        else ErrorMessage = "无法覆盖原始图片，请修改输出设置。";
    }

    private async Task OpenOutputAsync(CancellationToken cancellationToken)
    {
        if (!EnsureResultOutputAvailable()) return;
        var directory = Path.GetDirectoryName(ResultOutputPath);
        if (string.IsNullOrWhiteSpace(directory) || !await _launcher.OpenDirectoryAsync(directory, cancellationToken)) ErrorMessage = "无法打开输出目录，文件可能已被移动。";
    }

    private void ContinueResize()
    {
        if (!EnsureResultOutputAvailable()) return;
        if (_lastResult?.OutputPath is { } output) _ = ContinueResizeAsync(output);
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
    private async Task CopyDiagnosticIdAsync(CancellationToken cancellationToken) { if (DiagnosticId is not null) await _clipboard.SetTextAsync(DiagnosticId, cancellationToken); }

    private void ResetSelectionCore()
    {
        _selectedRatio = Ratios[0];
        _cropX = 0;
        _cropY = 0;
        _cropWidth = _probe?.Width ?? 1;
        _cropHeight = _probe?.Height ?? 1;
        OnPropertyChanged(nameof(SelectedRatio));
        OnPropertyChanged(nameof(CropX));
        OnPropertyChanged(nameof(CropY));
        OnPropertyChanged(nameof(CropWidth));
        OnPropertyChanged(nameof(CropHeight));
    }

    private void ApplyRatioPreset(CropRatioOption option)
    {
        if (_probe is null || option.Ratio <= 0) return;
        var inputRatio = _probe.Width / (double)_probe.Height;
        int width;
        int height;
        if (inputRatio >= option.Ratio)
        {
            height = _probe.Height;
            width = Math.Max(1, (int)Math.Floor(height * option.Ratio));
        }
        else
        {
            width = _probe.Width;
            height = Math.Max(1, (int)Math.Floor(width / option.Ratio));
        }
        _synchronizingSelection = true;
        _cropWidth = width;
        _cropHeight = height;
        _cropX = (_probe.Width - width) / 2;
        _cropY = (_probe.Height - height) / 2;
        _synchronizingSelection = false;
        OnPropertyChanged(nameof(CropX));
        OnPropertyChanged(nameof(CropY));
        OnPropertyChanged(nameof(CropWidth));
        OnPropertyChanged(nameof(CropHeight));
    }

    private bool TryBuildCropRectangle(out CropRectangle? rectangle, out string? error)
    {
        rectangle = null;
        error = null;
        if (_probe is null) { error = "请先选择图片。"; return false; }
        if (_probe.IsAnimated || _probe.FrameCount != 1 || _probe.Format is not (ImageFormatKind.Jpeg or ImageFormatKind.Png or ImageFormatKind.WebP or ImageFormatKind.Bmp))
        {
            error = "当前格式不能直接裁剪，请先转换为 JPEG、PNG、BMP 或单帧 WebP。";
            return false;
        }
        try
        {
            rectangle = new CropRectangle(ToNonNegativeInteger(CropX), ToNonNegativeInteger(CropY), ToPositiveInteger(CropWidth), ToPositiveInteger(CropHeight));
            var validation = CropRules.ValidateCropRectangle(new ImageSize(_probe.Width, _probe.Height), rectangle);
            if (!validation.Succeeded) { error = "选区必须完全位于原图内。"; rectangle = null; return false; }
            return true;
        }
        catch (ArgumentException)
        {
            error = "位置必须是非负整数，宽度和高度必须是正整数。";
            return false;
        }
    }

    private static int ToNonNegativeInteger(decimal value)
    {
        if (value < 0 || value > int.MaxValue || value != decimal.Truncate(value)) throw new ArgumentOutOfRangeException(nameof(value));
        return decimal.ToInt32(value);
    }
    private static int ToPositiveInteger(decimal value)
    {
        if (value <= 0 || value > int.MaxValue || value != decimal.Truncate(value)) throw new ArgumentOutOfRangeException(nameof(value));
        return decimal.ToInt32(value);
    }
    private static int ToCanvasInteger(decimal value) => value is >= 0 and <= int.MaxValue ? decimal.ToInt32(decimal.Truncate(value)) : 0;
    private static DesktopExecutionState ToExecutionState(ImageJobStatus status) => status switch
    {
        ImageJobStatus.Succeeded => DesktopExecutionState.Success,
        ImageJobStatus.Canceled => DesktopExecutionState.Canceled,
        ImageJobStatus.Skipped => DesktopExecutionState.Skipped,
        _ => DesktopExecutionState.Failure
    };

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
    private void SetError(AtomPixError? error) { ErrorMessage = DesktopErrorText.FromWorkflow(error); DiagnosticId = DesktopErrorText.DiagnosticId(error); }
    private int BeginLoad(CancellationToken cancellationToken, out CancellationTokenSource source) { CancelLoad(); source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); _loadCancellation = source; return ++_loadGeneration; }
    private bool IsCurrentLoad(int generation, CancellationTokenSource source) => generation == _loadGeneration && !source.IsCancellationRequested;
    private void CancelLoad() { _loadCancellation?.Cancel(); _loadCancellation?.Dispose(); _loadCancellation = null; }

    private void NotifyInput()
    {
        OnPropertyChanged(nameof(InputName));
        OnPropertyChanged(nameof(InputPath));
        OnPropertyChanged(nameof(InputSummary));
        OnPropertyChanged(nameof(ImagePixelWidth));
        OnPropertyChanged(nameof(ImagePixelHeight));
        NotifySelection();
    }
    private void NotifySelection()
    {
        OnPropertyChanged(nameof(CanvasX));
        OnPropertyChanged(nameof(CanvasY));
        OnPropertyChanged(nameof(CanvasWidth));
        OnPropertyChanged(nameof(CanvasHeight));
        OnPropertyChanged(nameof(LockedAspectRatio));
        OnPropertyChanged(nameof(IsCustomRatio));
        OnPropertyChanged(nameof(EstimatedOutput));
        OnPropertyChanged(nameof(DraftError));
        OnPropertyChanged(nameof(HasDraftError));
        OnPropertyChanged(nameof(CanStart));
        StartCommand.NotifyCanExecuteChanged();
    }
    private void OutputDraftChanged()
    {
        MarkDraftChanged();
        NotifySelection();
    }
    private void NotifyState()
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsContentLoading));
        OnPropertyChanged(nameof(IsContentReady));
        OnPropertyChanged(nameof(IsProcessing));
        OnPropertyChanged(nameof(IsSuccess));
        OnPropertyChanged(nameof(CanStart));
        SelectImageCommand.NotifyCanExecuteChanged();
        StartCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
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
        OnPropertyChanged(nameof(ResultDetails));
        OnPropertyChanged(nameof(ResultSizeChange));
        OpenOutputCommand.NotifyCanExecuteChanged();
        ContinueResizeCommand.NotifyCanExecuteChanged();
    }
}
