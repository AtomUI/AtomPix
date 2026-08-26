namespace AtomPix.Desktop.ViewModels;

using AtomPix.Core.Compression;
using AtomPix.Core.Conversion;
using AtomPix.Core.Errors;
using AtomPix.Core.Jobs;
using AtomPix.Core.Output;
using AtomPix.Core.ValueObjects;
using AtomPix.Desktop.Navigation;
using AtomPix.Desktop.Platform;
using AtomPix.Imaging.Abstractions.Processing;
using AtomPix.Workflows.Images;
using AtomPix.Workflows.Settings;

public sealed class ConversionEditorViewModel : ObservableObject, IDisposable, IToolEditorActions, IResultAvailabilityAware, IOperationResultFeedbackSource
{
    private readonly IDesktopPickerService _picker;
    private readonly IDesktopLauncherService _launcher;
    private readonly IDesktopDialogService _dialogs;
    private readonly IDesktopClipboardService _clipboard;
    private readonly ResultOutputGuard _outputGuard;
    private readonly OpenImageWorkflow _openImage;
    private readonly LoadSettingsWorkflow _loadSettings;
    private readonly ConvertImageWorkflow _convert;
    private readonly DesktopNavigationCoordinator _navigation;
    private CancellationTokenSource? _loadCancellation;
    private CancellationTokenSource? _executionCancellation;
    private int _loadGeneration;
    private DesktopContentState _contentState = DesktopContentState.Empty;
    private DesktopExecutionState _executionState = DesktopExecutionState.Draft;
    private LocalPath? _inputPath;
    private ImageProbeResult? _probe;
    private DesktopChoiceOption<OutputImageFormat> _selectedFormat;
    private decimal _quality = 80;
    private string _backgroundHex = "#FFFFFF";
    private bool _removeMetadata = true;
    private string? _errorMessage;
    private string? _diagnosticId;
    private ImageJobResult? _lastResult;
    private TransparencyProcessingResult? _transparencyResult;

    public ConversionEditorViewModel(
        IDesktopPickerService picker,
        IDesktopLauncherService launcher,
        IDesktopDialogService dialogs,
        IDesktopClipboardService clipboard,
        ResultOutputGuard outputGuard,
        OpenImageWorkflow openImage,
        LoadSettingsWorkflow loadSettings,
        ConvertImageWorkflow convert,
        DesktopNavigationCoordinator navigation)
    {
        _picker = picker ?? throw new ArgumentNullException(nameof(picker));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _outputGuard = outputGuard ?? throw new ArgumentNullException(nameof(outputGuard));
        _openImage = openImage ?? throw new ArgumentNullException(nameof(openImage));
        _loadSettings = loadSettings ?? throw new ArgumentNullException(nameof(loadSettings));
        _convert = convert ?? throw new ArgumentNullException(nameof(convert));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        Output = new OutputPolicyEditorViewModel(_picker, OutputDraftChanged, ResultFeedback.ShowWarning);

        Formats =
        [
            new DesktopChoiceOption<OutputImageFormat>("JPEG", OutputImageFormat.Jpeg),
            new DesktopChoiceOption<OutputImageFormat>("PNG", OutputImageFormat.Png),
            new DesktopChoiceOption<OutputImageFormat>("WebP", OutputImageFormat.WebP)
        ];
        _selectedFormat = Formats[2];
        SelectImageCommand = new AsyncCommand(SelectImageAsync, () => !IsProcessing);
        StartCommand = new AsyncCommand(StartAsync, () => IsContentReady && !IsProcessing);
        CancelCommand = new RelayCommand<object?>(_ => _executionCancellation?.Cancel(), _ => IsProcessing);
        UseWhiteCommand = new RelayCommand<object?>(_ => BackgroundHex = "#FFFFFF", _ => !IsProcessing);
        UseBlackCommand = new RelayCommand<object?>(_ => BackgroundHex = "#000000", _ => !IsProcessing);
        OpenOutputCommand = new AsyncCommand(OpenOutputAsync, () => IsSuccess);
        ContinueResizeCommand = new RelayCommand<object?>(_ => ContinueResize(), _ => IsSuccess);
        CopyDiagnosticIdCommand = new AsyncCommand(CopyDiagnosticIdAsync, () => HasDiagnosticId);
    }

    public IReadOnlyList<DesktopChoiceOption<OutputImageFormat>> Formats { get; }
    public OutputPolicyEditorViewModel Output { get; }
    public OperationResultFeedback ResultFeedback { get; } = new();
    public DesktopContentState ContentState { get => _contentState; private set { if (SetProperty(ref _contentState, value)) NotifyState(); } }
    public DesktopExecutionState ExecutionState { get => _executionState; private set { if (SetProperty(ref _executionState, value)) NotifyState(); } }
    public DesktopChoiceOption<OutputImageFormat> SelectedFormat
    {
        get => _selectedFormat;
        set { if (value is not null && SetProperty(ref _selectedFormat, value)) { MarkDraftChanged(); NotifyDraft(); } }
    }
    public decimal Quality
    {
        get => _quality;
        set { if (SetProperty(ref _quality, value)) { OnPropertyChanged(nameof(QualitySlider)); MarkDraftChanged(); NotifyDraft(); } }
    }
    public double QualitySlider
    {
        get => decimal.ToDouble(Quality);
        set => Quality = decimal.Round(
            Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture),
            0,
            MidpointRounding.AwayFromZero);
    }

    public string StartActionLabel => "单张处理";

    System.Windows.Input.ICommand IToolEditorActions.StartActionCommand => StartCommand;

    System.Windows.Input.ICommand IToolEditorActions.CancelActionCommand => CancelCommand;
    public string BackgroundHex
    {
        get => _backgroundHex;
        set { if (SetProperty(ref _backgroundHex, value ?? string.Empty)) { MarkDraftChanged(); NotifyDraft(); } }
    }
    public bool RemoveMetadata
    {
        get => _removeMetadata;
        set { if (SetProperty(ref _removeMetadata, value)) { MarkDraftChanged(); NotifyDraft(); } }
    }

    public bool IsEmpty => ContentState == DesktopContentState.Empty;
    public bool IsContentLoading => ContentState == DesktopContentState.Loading;
    public bool IsContentReady => ContentState == DesktopContentState.Ready;
    public bool IsProcessing => ExecutionState == DesktopExecutionState.Processing;
    public bool IsLossyFormat => SelectedFormat.Value is OutputImageFormat.Jpeg or OutputImageFormat.WebP;
    public bool ShowTransparencyBackground => _probe?.HasTransparency == true && SelectedFormat.Value == OutputImageFormat.Jpeg;
    public string TransparencySummary => _probe?.HasTransparency != true
        ? "未检测到透明区域"
        : SelectedFormat.Value == OutputImageFormat.Jpeg
            ? $"透明区域将使用 {NormalizedBackgroundHex} 填充"
            : "透明区域将保留";
    public string NormalizedBackgroundHex => RgbColor.TryParse(BackgroundHex, out var color) ? color.ToHexString() : BackgroundHex;
    public string? PreviewBackgroundHex => ShowTransparencyBackground && RgbColor.TryParse(BackgroundHex, out var color)
        ? color.ToHexString()
        : null;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasDiagnosticId => !string.IsNullOrWhiteSpace(DiagnosticId);
    public bool HasResult => _lastResult is not null;
    public bool IsSuccess => ExecutionState == DesktopExecutionState.Success;
    public bool CanStart => IsContentReady && !IsProcessing && TryBuildProfile(out _, out _) && Output.IsValid;
    public string InputName => _inputPath is null ? string.Empty : Path.GetFileName(_inputPath.Value.Value);
    public string InputPath => _inputPath?.Value ?? string.Empty;
    public string InputSummary => _probe is null ? string.Empty : $"{_probe.Width} × {_probe.Height}  ·  {_probe.Format.ToString().ToUpperInvariant()}";
    public string? DraftError
    {
        get
        {
            TryBuildProfile(out _, out var error);
            return error;
        }
    }
    public bool HasDraftError => !string.IsNullOrWhiteSpace(DraftError);
    public string? ErrorMessage { get => _errorMessage; private set { if (SetProperty(ref _errorMessage, value)) OnPropertyChanged(nameof(HasError)); } }
    public string? DiagnosticId { get => _diagnosticId; private set { if (SetProperty(ref _diagnosticId, value)) { OnPropertyChanged(nameof(HasDiagnosticId)); CopyDiagnosticIdCommand.NotifyCanExecuteChanged(); } } }
    public string ResultTitle => _lastResult?.Status switch
    {
        ImageJobStatus.Succeeded => "转换完成",
        ImageJobStatus.Canceled => "任务已取消",
        ImageJobStatus.Skipped => "目标已存在，未生成新文件",
        ImageJobStatus.Failed => "转换失败",
        _ => string.Empty
    };
    public string ResultOutputPath => _lastResult?.OutputPath?.Value ?? string.Empty;
    public bool IsResultOutputAvailable => IsSuccess && _outputGuard.FileExists(_lastResult?.OutputPath);
    public bool IsResultOutputMissing => IsSuccess && _lastResult?.OutputPath is not null && !IsResultOutputAvailable;
    public string ResultSizeChange => DesktopResultText.FormatSizeChange(_lastResult);
    public string ResultTransparency => _transparencyResult?.Outcome switch
    {
        TransparencyOutcome.NotPresent => "无透明区域",
        TransparencyOutcome.Preserved => "已保留透明区域",
        TransparencyOutcome.Flattened => $"已使用 {_transparencyResult.BackgroundColor!.ToHexString()} 填充透明区域",
        _ => string.Empty
    };

    public AsyncCommand SelectImageCommand { get; }
    public AsyncCommand StartCommand { get; }
    public RelayCommand<object?> CancelCommand { get; }
    public RelayCommand<object?> UseWhiteCommand { get; }
    public RelayCommand<object?> UseBlackCommand { get; }
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
        _transparencyResult = null;
        ExecutionState = DesktopExecutionState.Draft;
        ContentState = DesktopContentState.Loading;
        NotifyInput();
        NotifyResult();

        if (initializeDraft)
        {
            var settings = await _loadSettings.ExecuteAsync(new LoadSettingsRequest(), loadCancellation.Token);
            if (!IsCurrentLoad(generation, loadCancellation)) return;
            if (!settings.Succeeded) { SetError(settings.Error); ContentState = DesktopContentState.Failure; return; }
            ApplyDefaults(settings.Value!.Settings.DefaultConversionProfile, settings.Value.Settings.DefaultOutputPolicy);
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
        _transparencyResult = null;
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
        if (selection.Status != DesktopSelectionStatus.Selected || selection.Paths.Count != 1) { ErrorMessage = DesktopErrorText.FromPicker(selection.ErrorMessage); return; }
        var path = new LocalPath(selection.Paths[0]);
        var opened = await _openImage.ExecuteAsync(new OpenImageRequest(path), cancellationToken);
        if (!opened.Succeeded) { SetError(opened.Error); return; }
        await LoadAsync(new SingleImageNavigationContext(path, opened.Value!.ProbeResult), cancellationToken);
    }

    private async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_inputPath is null) return;
        if (!TryBuildProfile(out var profile, out var profileError))
        {
            ResultFeedback.ShowWarning(profileError ?? "当前转换配置无效。");
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
            var result = await _convert.ExecuteAsync(new ConvertImageRequest(_inputPath.Value, profile!, outputPolicy!), executionCancellation.Token);
            if (!result.Succeeded)
            {
                if (result.Error?.Code == AtomPixErrorCode.OutputPathConflictsWithInput) await HandleSourceConflictAsync(executionCancellation.Token);
                else SetError(result.Error);
                ExecutionState = result.Error?.Code == AtomPixErrorCode.OperationCanceled ? DesktopExecutionState.Canceled : DesktopExecutionState.Failure;
                if (ExecutionState == DesktopExecutionState.Canceled) ResultFeedback.ShowCanceled();
                return;
            }

            _lastResult = result.Value!.JobResult;
            _transparencyResult = result.Value.Transparency;
            ExecutionState = ToExecutionState(_lastResult.Status);
            if (_lastResult.Status == ImageJobStatus.Failed) SetError(_lastResult.Error);
            else ResultFeedback.Show(_lastResult, result.Value.OutputDisposition, "转换完成", ResultSizeChange);
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

    private bool TryBuildProfile(out ConversionProfile? profile, out string? error)
    {
        profile = null;
        error = null;
        if (_probe is null) { error = "请先选择图片。"; return false; }
        if (_probe.IsAnimated || _probe.FrameCount != 1) { error = "动画或多帧图片暂不参与转换。"; return false; }
        try
        {
            ImageQuality? quality = IsLossyFormat ? new ImageQuality(ToQuality(Quality)) : null;
            if (!RgbColor.TryParse(BackgroundHex, out var background)) throw new FormatException();
            profile = new ConversionProfile(
                SelectedFormat.Value,
                quality,
                RemoveMetadata ? MetadataPolicy.Remove : MetadataPolicy.Preserve,
                new TransparencyPolicy(background));
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            error = !RgbColor.TryParse(BackgroundHex, out _) ? "背景色必须使用六位 #RRGGBB 格式。" : "输出质量必须是 1 到 100 的整数。";
            return false;
        }
    }

    private void ApplyDefaults(ConversionProfile profile, OutputPolicy outputPolicy)
    {
        _selectedFormat = Formats.First(item => item.Value == profile.OutputFormat);
        _quality = profile.Quality?.Value ?? 80;
        _backgroundHex = profile.TransparencyPolicy.OpaqueBackgroundColor.ToHexString();
        _removeMetadata = profile.MetadataPolicy == MetadataPolicy.Remove;
        Output.Apply(outputPolicy);
        OnPropertyChanged(nameof(SelectedFormat));
        OnPropertyChanged(nameof(Quality));
        OnPropertyChanged(nameof(QualitySlider));
        OnPropertyChanged(nameof(BackgroundHex));
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
        OnPropertyChanged(nameof(ShowTransparencyBackground));
        OnPropertyChanged(nameof(TransparencySummary));
        NotifyDraft();
    }

    private void NotifyDraft()
    {
        OnPropertyChanged(nameof(IsLossyFormat));
        OnPropertyChanged(nameof(ShowTransparencyBackground));
        OnPropertyChanged(nameof(TransparencySummary));
        OnPropertyChanged(nameof(NormalizedBackgroundHex));
        OnPropertyChanged(nameof(PreviewBackgroundHex));
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
        OnPropertyChanged(nameof(ResultTransparency));
        OpenOutputCommand.NotifyCanExecuteChanged();
        ContinueResizeCommand.NotifyCanExecuteChanged();
    }
}
