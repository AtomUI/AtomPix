namespace AtomPix.Desktop.ViewModels;

using System.Collections.ObjectModel;
using AtomPix.Core.Compression;
using AtomPix.Core.Conversion;
using AtomPix.Core.Errors;
using AtomPix.Core.Jobs;
using AtomPix.Core.Output;
using AtomPix.Core.Resize;
using AtomPix.Core.ValueObjects;
using AtomPix.Desktop.Navigation;
using AtomPix.Desktop.Platform;
using AtomPix.Imaging.Abstractions.Formats;
using AtomPix.Imaging.Abstractions.Processing;
using AtomPix.Workflows.Images;
using AtomPix.Workflows.Settings;

public enum BatchTaskKind
{
    Compress,
    Convert,
    Resize
}

public sealed class BatchItemViewModel : ObservableObject
{
    private LocalPath _path;
    private ImageProbeResult? _probe;
    private string _statusText = "等待处理";
    private string? _outputPath;
    private string? _errorText;
    private string _estimatedSize = "—";
    private ImageJobStatus? _terminalStatus;
    private AtomPixErrorCode? _errorCode;
    private string? _diagnosticId;
    private bool _canRemove = true;

    public BatchItemViewModel(LocalPath path)
    {
        _path = path;
    }

    public LocalPath Path { get => _path; private set { if (SetProperty(ref _path, value)) { OnPropertyChanged(nameof(DisplayName)); OnPropertyChanged(nameof(FullPath)); } } }
    public string DisplayName => System.IO.Path.GetFileName(Path.Value);
    public string FullPath => Path.Value;
    public ImageProbeResult? Probe => _probe;
    public bool HasTransparency => _probe?.HasTransparency == true;
    public bool UsesLossyQuality => _probe?.Format is ImageFormatKind.Jpeg or ImageFormatKind.WebP;
    public string ProbeSummary => _probe is null ? "等待读取图片信息" : $"{_probe.Width} × {_probe.Height} · {_probe.Format}";
    public string StatusText { get => _statusText; private set { if (SetProperty(ref _statusText, value)) OnPropertyChanged(nameof(IsRunning)); } }
    public string OutputPath { get => _outputPath ?? string.Empty; private set => SetProperty(ref _outputPath, value); }
    public string? ErrorText { get => _errorText; private set { if (SetProperty(ref _errorText, value)) OnPropertyChanged(nameof(HasError)); } }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);
    public string EstimatedSize { get => _estimatedSize; private set => SetProperty(ref _estimatedSize, value); }
    public ImageJobStatus? TerminalStatus { get => _terminalStatus; private set => SetProperty(ref _terminalStatus, value); }
    public AtomPixErrorCode? ErrorCode { get => _errorCode; private set => SetProperty(ref _errorCode, value); }
    public string? DiagnosticId { get => _diagnosticId; private set { if (SetProperty(ref _diagnosticId, value)) OnPropertyChanged(nameof(HasDiagnosticId)); } }
    public bool HasDiagnosticId => !string.IsNullOrWhiteSpace(DiagnosticId);
    public bool CanViewDetails => TerminalStatus is not null || HasError;
    public bool CanRelocate => HasError && ErrorCode is AtomPixErrorCode.InputFileNotFound or AtomPixErrorCode.InvalidImageFile;
    public bool CanRemove { get => _canRemove; private set => SetProperty(ref _canRemove, value); }
    public bool IsRunning => StatusText == "处理中";

    public void SetProbe(ImageProbeResult? probe)
    {
        _probe = probe;
        OnPropertyChanged(nameof(Probe));
        OnPropertyChanged(nameof(HasTransparency));
        OnPropertyChanged(nameof(UsesLossyQuality));
        OnPropertyChanged(nameof(ProbeSummary));
    }

    public void SetPlan(string outputPath)
    {
        OutputPath = outputPath;
        StatusText = "等待处理";
        TerminalStatus = null;
        ErrorText = null;
        ErrorCode = null;
        DiagnosticId = null;
    }

    public void SetRunning()
    {
        StatusText = "处理中";
        TerminalStatus = null;
        ErrorText = null;
        ErrorCode = null;
        DiagnosticId = null;
    }

    public void SetTerminal(ImageJobResult result)
    {
        TerminalStatus = result.Status;
        if (result.OutputPath is not null) OutputPath = result.OutputPath.Value.Value;
        ErrorText = result.Error is null ? null : DesktopErrorText.FromWorkflow(result.Error);
        ErrorCode = result.Error?.Code;
        DiagnosticId = DesktopErrorText.DiagnosticId(result.Error);
        StatusText = result.Status switch
        {
            ImageJobStatus.Succeeded => "成功",
            ImageJobStatus.Failed => "失败",
            ImageJobStatus.Canceled => "已取消",
            ImageJobStatus.Skipped => "已跳过",
            _ => result.Status.ToString()
        };
        OnPropertyChanged(nameof(CanViewDetails));
        OnPropertyChanged(nameof(CanRelocate));
    }

    public void SetNotStarted()
    {
        TerminalStatus = null;
        StatusText = "未开始";
        OnPropertyChanged(nameof(CanViewDetails));
        OnPropertyChanged(nameof(CanRelocate));
    }

    public void SetEstimatedSize(string value) => EstimatedSize = value;

    public void SetCanRemove(bool value) => CanRemove = value;

    public void SetProbeError(AtomPixError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        _probe = null;
        StatusText = "不可用";
        TerminalStatus = null;
        ErrorText = DesktopErrorText.FromWorkflow(error);
        ErrorCode = error.Code;
        DiagnosticId = DesktopErrorText.DiagnosticId(error);
        OnPropertyChanged(nameof(Probe));
        OnPropertyChanged(nameof(ProbeSummary));
        OnPropertyChanged(nameof(CanViewDetails));
        OnPropertyChanged(nameof(CanRelocate));
    }

    public void ReplacePath(LocalPath path, ImageProbeResult probe)
    {
        Path = path;
        SetProbe(probe);
        StatusText = "等待处理";
        OutputPath = string.Empty;
        TerminalStatus = null;
        ErrorText = null;
        ErrorCode = null;
        DiagnosticId = null;
        OnPropertyChanged(nameof(CanViewDetails));
        OnPropertyChanged(nameof(CanRelocate));
    }
}

public sealed record SubmittedBatchSnapshot(
    BatchTaskKind Kind,
    IReadOnlyList<LocalPath> Inputs,
    object ProcessingProfile,
    OutputPolicy OutputPolicy);

public sealed class BatchTaskViewModel : ObservableObject, IDisposable, IDesktopForegroundTask, IResultAvailabilityAware
{
    private readonly IDesktopPickerService _picker;
    private readonly IDesktopDialogService _dialogs;
    private readonly IDesktopLauncherService _launcher;
    private readonly IDesktopClipboardService _clipboard;
    private readonly IDesktopDispatcher _dispatcher;
    private readonly ResultOutputGuard _outputGuard;
    private readonly AppendBatchInputsWorkflow _appendInputs;
    private readonly OpenImageWorkflow _openImage;
    private readonly LoadSettingsWorkflow _loadSettings;
    private readonly BatchCompressWorkflow _batchCompress;
    private readonly BatchConvertWorkflow _batchConvert;
    private readonly BatchResizeWorkflow _batchResize;
    private readonly DesktopNavigationCoordinator _navigation;
    private CancellationTokenSource? _executionCancellation;
    private bool _initialized;
    private bool _isAppending;
    private bool _isProcessing;
    private bool _isCanceling;
    private DesktopChoiceOption<BatchTaskKind> _selectedTask;
    private DesktopChoiceOption<CompressionMode> _selectedCompressionMode;
    private decimal _customQuality = 80;
    private DesktopChoiceOption<OutputImageFormat> _selectedFormat;
    private decimal _conversionQuality = 80;
    private string _backgroundHex = "#FFFFFF";
    private bool _removeMetadata = true;
    private DesktopChoiceOption<ResizeDraftMode> _selectedResizeMode;
    private decimal? _pixelWidth;
    private decimal? _pixelHeight;
    private PixelDimensionAnchor _pixelAnchor = PixelDimensionAnchor.Width;
    private bool _maintainAspectRatio = true;
    private bool _preventUpscaling;
    private decimal _percentage = 50;
    private SameFormatEncodingPolicy _sameFormatEncoding = SameFormatEncodingPolicy.Default;
    private string? _errorMessage;
    private string? _noticeMessage;
    private string? _diagnosticId;
    private double _progressRatio;
    private string _progressSummary = "尚未开始";
    private BatchResult? _lastBatchResult;
    private BatchResult? _previousBatchResult;
    private SubmittedBatchSnapshot? _submittedSnapshot;
    private long _executionGeneration;
    private BatchJobId? _acceptedBatchId;
    private long _lastSequence;

    public BatchTaskViewModel(
        IDesktopPickerService picker,
        IDesktopDialogService dialogs,
        IDesktopLauncherService launcher,
        IDesktopClipboardService clipboard,
        IDesktopDispatcher dispatcher,
        ResultOutputGuard outputGuard,
        AppendBatchInputsWorkflow appendInputs,
        OpenImageWorkflow openImage,
        LoadSettingsWorkflow loadSettings,
        BatchCompressWorkflow batchCompress,
        BatchConvertWorkflow batchConvert,
        BatchResizeWorkflow batchResize,
        DesktopNavigationCoordinator navigation)
    {
        _picker = picker ?? throw new ArgumentNullException(nameof(picker));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _outputGuard = outputGuard ?? throw new ArgumentNullException(nameof(outputGuard));
        _appendInputs = appendInputs ?? throw new ArgumentNullException(nameof(appendInputs));
        _openImage = openImage ?? throw new ArgumentNullException(nameof(openImage));
        _loadSettings = loadSettings ?? throw new ArgumentNullException(nameof(loadSettings));
        _batchCompress = batchCompress ?? throw new ArgumentNullException(nameof(batchCompress));
        _batchConvert = batchConvert ?? throw new ArgumentNullException(nameof(batchConvert));
        _batchResize = batchResize ?? throw new ArgumentNullException(nameof(batchResize));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        Output = new OutputPolicyEditorViewModel(_picker, NotifyDraft);

        TaskKinds =
        [
            new DesktopChoiceOption<BatchTaskKind>("压缩", BatchTaskKind.Compress),
            new DesktopChoiceOption<BatchTaskKind>("转换", BatchTaskKind.Convert),
            new DesktopChoiceOption<BatchTaskKind>("调整尺寸", BatchTaskKind.Resize)
        ];
        CompressionModes = Enum.GetValues<CompressionMode>().Select(mode => new DesktopChoiceOption<CompressionMode>(mode switch
        {
            CompressionMode.Smart => "智能",
            CompressionMode.HighQuality => "高质量",
            CompressionMode.Balanced => "平衡",
            CompressionMode.Maximum => "极限",
            _ => "自定义"
        }, mode)).ToArray();
        ConversionFormats =
        [
            new DesktopChoiceOption<OutputImageFormat>("JPEG", OutputImageFormat.Jpeg),
            new DesktopChoiceOption<OutputImageFormat>("PNG", OutputImageFormat.Png),
            new DesktopChoiceOption<OutputImageFormat>("WebP", OutputImageFormat.WebP)
        ];
        ResizeModes =
        [
            new DesktopChoiceOption<ResizeDraftMode>("按像素", ResizeDraftMode.Pixel),
            new DesktopChoiceOption<ResizeDraftMode>("按百分比", ResizeDraftMode.Percentage)
        ];
        _selectedTask = TaskKinds[0];
        _selectedCompressionMode = CompressionModes[0];
        _selectedFormat = ConversionFormats[2];
        _selectedResizeMode = ResizeModes[0];

        AddFilesCommand = new AsyncCommand(AddFilesAsync, () => CanEditInputs);
        AddFolderCommand = new AsyncCommand(AddFolderAsync, () => CanEditInputs);
        RemoveInputCommand = new RelayCommand<BatchItemViewModel>(RemoveInput, item => item is not null && CanEditInputs);
        InsertNameTokenCommand = new RelayCommand<object?>(_ => FileNamePattern += "{name}", _ => CanEditDraft);
        InsertIndexTokenCommand = new RelayCommand<object?>(_ => FileNamePattern += "{index}", _ => CanEditDraft && !FileNamePattern.Contains("{index}", StringComparison.Ordinal));
        StartCommand = new AsyncCommand(StartAsync, () => CanAttemptStart);
        CancelCommand = new AsyncCommand(CancelAsync, () => IsProcessing && !IsCanceling);
        RetryFailedCommand = new RelayCommand<object?>(_ => RecoverFailed(), _ => HasFailedItems && !IsProcessing);
        ProcessUnfinishedCommand = new RelayCommand<object?>(_ => RecoverUnfinished(), _ => HasUnfinishedItems && !IsProcessing);
        ProcessSkippedWithAutoRenameCommand = new RelayCommand<object?>(_ => RecoverSkipped(), _ => HasSkippedItems && !IsProcessing);
        ContinueOtherCommand = new RelayCommand<object?>(_ => ResetToEmptyDraft(), _ => HasResult && !IsProcessing);
        OpenOutputCommand = new AsyncCommand(OpenOutputAsync, () => HasResult && !IsProcessing && !string.IsNullOrWhiteSpace(OutputDirectory));
        OpenInputDirectoryCommand = new AsyncCommand(OpenInputDirectoryAsync, () => Items.Count > 0 && !IsProcessing);
        ViewItemDetailsCommand = new AsyncCommand<BatchItemViewModel>(ViewItemDetailsAsync, item => item is { CanViewDetails: true });
        RelocateInputCommand = new AsyncCommand<BatchItemViewModel>(RelocateInputAsync, item => item is { CanRelocate: true } && !IsProcessing);
        CopyItemDiagnosticIdCommand = new AsyncCommand<BatchItemViewModel>(CopyItemDiagnosticIdAsync, item => item is { HasDiagnosticId: true });
        CopyDiagnosticIdCommand = new AsyncCommand(CopyDiagnosticIdAsync, () => HasDiagnosticId);
        UseWhiteCommand = new RelayCommand<object?>(_ => BackgroundHex = "#FFFFFF", _ => CanEditDraft);
        UseBlackCommand = new RelayCommand<object?>(_ => BackgroundHex = "#000000", _ => CanEditDraft);
    }

    public ObservableCollection<BatchItemViewModel> Items { get; } = [];
    public event EventHandler<IReadOnlyList<LocalPath>>? RecoveryDraftCreated;
    public IReadOnlyList<DesktopChoiceOption<BatchTaskKind>> TaskKinds { get; }
    public IReadOnlyList<DesktopChoiceOption<CompressionMode>> CompressionModes { get; }
    public IReadOnlyList<DesktopChoiceOption<OutputImageFormat>> ConversionFormats { get; }
    public IReadOnlyList<DesktopChoiceOption<ResizeDraftMode>> ResizeModes { get; }
    public OutputPolicyEditorViewModel Output { get; }

    public DesktopChoiceOption<BatchTaskKind> SelectedTask { get => _selectedTask; set { if (value is not null && SetProperty(ref _selectedTask, value)) NotifyDraft(); } }
    public DesktopChoiceOption<CompressionMode> SelectedCompressionMode { get => _selectedCompressionMode; set { if (value is not null && SetProperty(ref _selectedCompressionMode, value)) NotifyDraft(); } }
    public decimal CustomQuality { get => _customQuality; set { if (SetProperty(ref _customQuality, value)) { OnPropertyChanged(nameof(CustomQualitySlider)); NotifyDraft(); } } }
    public double CustomQualitySlider
    {
        get => decimal.ToDouble(CustomQuality);
        set => CustomQuality = decimal.Round(Convert.ToDecimal(value), 0, MidpointRounding.AwayFromZero);
    }
    public DesktopChoiceOption<OutputImageFormat> SelectedFormat { get => _selectedFormat; set { if (value is not null && SetProperty(ref _selectedFormat, value)) NotifyDraft(); } }
    public decimal ConversionQuality { get => _conversionQuality; set { if (SetProperty(ref _conversionQuality, value)) { OnPropertyChanged(nameof(ConversionQualitySlider)); NotifyDraft(); } } }
    public double ConversionQualitySlider
    {
        get => decimal.ToDouble(ConversionQuality);
        set => ConversionQuality = decimal.Round(Convert.ToDecimal(value), 0, MidpointRounding.AwayFromZero);
    }
    public string BackgroundHex { get => _backgroundHex; set { if (SetProperty(ref _backgroundHex, value ?? string.Empty)) NotifyDraft(); } }
    public bool RemoveMetadata { get => _removeMetadata; set { if (SetProperty(ref _removeMetadata, value)) NotifyDraft(); } }
    public DesktopChoiceOption<ResizeDraftMode> SelectedResizeMode { get => _selectedResizeMode; set { if (value is not null && SetProperty(ref _selectedResizeMode, value)) NotifyDraft(); } }
    public decimal? PixelWidth { get => _pixelWidth; set { if (SetProperty(ref _pixelWidth, value)) { _pixelAnchor = PixelDimensionAnchor.Width; OnPropertyChanged(nameof(PixelAnchor)); NotifyDraft(); } } }
    public decimal? PixelHeight { get => _pixelHeight; set { if (SetProperty(ref _pixelHeight, value)) { _pixelAnchor = PixelDimensionAnchor.Height; OnPropertyChanged(nameof(PixelAnchor)); NotifyDraft(); } } }
    public PixelDimensionAnchor PixelAnchor => _pixelAnchor;
    public bool MaintainAspectRatio { get => _maintainAspectRatio; set { if (SetProperty(ref _maintainAspectRatio, value)) NotifyDraft(); } }
    public bool PreventUpscaling { get => _preventUpscaling; set { if (SetProperty(ref _preventUpscaling, value)) NotifyDraft(); } }
    public decimal Percentage { get => _percentage; set { if (SetProperty(ref _percentage, value)) NotifyDraft(); } }
    public string FileNamePattern { get => Output.FileNamePattern; set => Output.FileNamePattern = value; }
    public bool IsAppending { get => _isAppending; private set { if (SetProperty(ref _isAppending, value)) NotifyState(); } }
    public bool IsProcessing { get => _isProcessing; private set { if (SetProperty(ref _isProcessing, value)) NotifyState(); } }
    public bool IsCanceling { get => _isCanceling; private set { if (SetProperty(ref _isCanceling, value)) NotifyState(); } }
    public bool IsEmpty => Items.Count == 0;
    public bool HasInputs => Items.Count > 0;
    public string InputCollectionSummary => $"浏览走廊中的 {Items.Count} 张图片；顺序与缩略图状态保持一致。";
    public bool CanEditInputs => !IsAppending && !IsProcessing && !HasResult;
    public bool CanEditDraft => !IsProcessing && !HasResult;
    public bool IsCompressTask => SelectedTask.Value == BatchTaskKind.Compress;
    public bool IsConvertTask => SelectedTask.Value == BatchTaskKind.Convert;
    public bool IsResizeTask => SelectedTask.Value == BatchTaskKind.Resize;
    public bool IsCustomCompression => SelectedCompressionMode.Value == CompressionMode.Custom;
    public bool IsLossyConversion => SelectedFormat.Value is OutputImageFormat.Jpeg or OutputImageFormat.WebP;
    public bool IsPixelResize => SelectedResizeMode.Value == ResizeDraftMode.Pixel;
    public bool IsPercentageResize => SelectedResizeMode.Value == ResizeDraftMode.Percentage;
    public bool ShowBatchTransparency => IsConvertTask && SelectedFormat.Value == OutputImageFormat.Jpeg && TransparentItemCount > 0;
    public int TransparentItemCount => Items.Count(item => item.HasTransparency);
    public int LossyItemCount => Items.Count(item => item.UsesLossyQuality);
    public string? DraftError { get { TryBuildSubmission(out _, out var error); return error; } }
    public bool HasDraftError => !string.IsNullOrWhiteSpace(DraftError);
    public bool CanAttemptStart => HasInputs && !IsAppending && !IsProcessing && !HasResult;
    public bool CanStart => _initialized && HasInputs && !IsAppending && !IsProcessing && !HasResult && TryBuildSubmission(out _, out _);
    public string EffectivePattern => Items.Count > 1 && !FileNamePattern.Contains("{index}", StringComparison.Ordinal) ? FileNamePattern + "_{index}" : FileNamePattern;
    public bool PatternWillAppendIndex => Items.Count > 1 && !FileNamePattern.Contains("{index}", StringComparison.Ordinal) && !HasDraftError;
    public string OutputExamples => BuildOutputExamples();
    public string? ErrorMessage { get => _errorMessage; private set { if (SetProperty(ref _errorMessage, value)) OnPropertyChanged(nameof(HasError)); } }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public string? NoticeMessage { get => _noticeMessage; private set { if (SetProperty(ref _noticeMessage, value)) OnPropertyChanged(nameof(HasNotice)); } }
    public bool HasNotice => !string.IsNullOrWhiteSpace(NoticeMessage);
    public string? DiagnosticId { get => _diagnosticId; private set { if (SetProperty(ref _diagnosticId, value)) { OnPropertyChanged(nameof(HasDiagnosticId)); CopyDiagnosticIdCommand.NotifyCanExecuteChanged(); } } }
    public bool HasDiagnosticId => !string.IsNullOrWhiteSpace(DiagnosticId);
    public double ProgressRatio { get => _progressRatio; private set => SetProperty(ref _progressRatio, value); }
    public string ProgressSummary { get => _progressSummary; private set => SetProperty(ref _progressSummary, value); }
    public bool HasResult => _lastBatchResult is not null;
    public bool HasPreviousResult => _previousBatchResult is not null;
    public string PreviousResultSummary => _previousBatchResult is null ? string.Empty : $"上一任务：{FormatBatchStatus(_previousBatchResult.Status)} · 成功 {_previousBatchResult.SucceededCount} · 失败 {_previousBatchResult.FailedCount} · 跳过 {_previousBatchResult.SkippedCount}";
    public string ResultTitle => _lastBatchResult is null ? string.Empty : FormatBatchStatus(_lastBatchResult.Status);
    public string ResultSummary => _lastBatchResult is null ? string.Empty : $"成功 {_lastBatchResult.SucceededCount} · 失败 {_lastBatchResult.FailedCount} · 跳过 {_lastBatchResult.SkippedCount} · 取消 {_lastBatchResult.CanceledCount}";
    public string ResultSizeChange => FormatBatchSizeChange(_lastBatchResult);
    public bool HasFailedItems => _lastBatchResult?.Items.Any(item => item.Status == ImageJobStatus.Failed) == true;
    public bool HasSkippedItems => _lastBatchResult?.Items.Any(item => item.Status == ImageJobStatus.Skipped) == true;
    public bool HasUnfinishedItems => _lastBatchResult is not null && _submittedSnapshot is not null
        && (_lastBatchResult.Status == BatchJobStatus.Canceled || _lastBatchResult.Items.Count < _submittedSnapshot.Inputs.Count || HasFailedItems);
    public string OutputDirectory
    {
        get
        {
            var output = _lastBatchResult?.Items.FirstOrDefault(item => item.OutputPath is not null)?.OutputPath?.Value;
            return output is null ? string.Empty : System.IO.Path.GetDirectoryName(output) ?? string.Empty;
        }
    }
    public string InputDirectory => Items.Count == 0 ? string.Empty : System.IO.Path.GetDirectoryName(Items[0].Path.Value) ?? string.Empty;
    public bool HasAvailableOutput => _lastBatchResult?.Items.Any(item => _outputGuard.FileExists(item.OutputPath)) == true;
    public bool HasMissingOutput => HasResult && _lastBatchResult!.Items.Any(item => item.Status == ImageJobStatus.Succeeded && !_outputGuard.FileExists(item.OutputPath));

    public AsyncCommand AddFilesCommand { get; }
    public AsyncCommand AddFolderCommand { get; }
    public RelayCommand<BatchItemViewModel> RemoveInputCommand { get; }
    public RelayCommand<object?> InsertNameTokenCommand { get; }
    public RelayCommand<object?> InsertIndexTokenCommand { get; }
    public AsyncCommand StartCommand { get; }
    public AsyncCommand CancelCommand { get; }
    public RelayCommand<object?> RetryFailedCommand { get; }
    public RelayCommand<object?> ProcessUnfinishedCommand { get; }
    public RelayCommand<object?> ProcessSkippedWithAutoRenameCommand { get; }
    public RelayCommand<object?> ContinueOtherCommand { get; }
    public AsyncCommand OpenOutputCommand { get; }
    public AsyncCommand OpenInputDirectoryCommand { get; }
    public AsyncCommand<BatchItemViewModel> ViewItemDetailsCommand { get; }
    public AsyncCommand<BatchItemViewModel> RelocateInputCommand { get; }
    public AsyncCommand<BatchItemViewModel> CopyItemDiagnosticIdCommand { get; }
    public AsyncCommand CopyDiagnosticIdCommand { get; }
    public RelayCommand<object?> UseWhiteCommand { get; }
    public RelayCommand<object?> UseBlackCommand { get; }

    public void RequestCancellation() => _executionCancellation?.Cancel();

    public void RefreshResultAvailability() => NotifyResult();

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized) return;
        ClearError();
        var settings = await _loadSettings.ExecuteAsync(new LoadSettingsRequest(), cancellationToken);
        if (!settings.Succeeded) { SetWorkflowError(settings.Error); return; }
        var value = settings.Value!.Settings;
        _selectedCompressionMode = CompressionModes.First(item => item.Value == value.DefaultCompressionProfile.Mode);
        _customQuality = value.DefaultCompressionProfile.Quality?.Value ?? 80;
        _selectedFormat = ConversionFormats.First(item => item.Value == value.DefaultConversionProfile.OutputFormat);
        _conversionQuality = value.DefaultConversionProfile.Quality?.Value ?? 80;
        _backgroundHex = value.DefaultConversionProfile.TransparencyPolicy.OpaqueBackgroundColor.ToHexString();
        _removeMetadata = value.DefaultCompressionProfile.MetadataPolicy == MetadataPolicy.Remove;
        Output.Apply(value.DefaultOutputPolicy);
        _sameFormatEncoding = value.DefaultSameFormatEncodingPolicy;
        _initialized = true;
        OnPropertyChanged(string.Empty);
        NotifyDraft();
    }

    public async Task PrepareAsync(
        BatchTaskKind kind,
        IReadOnlyList<LocalPath> inputs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (IsProcessing) return;

        // The unified tool drawer submits a complete visible draft immediately after
        // this preparation step. Batch execution must therefore not reload defaults
        // (or depend on a settings read succeeding) at click time. Defaults remain the
        // responsibility of LoadAsync for standalone/new drafts; this path is initialized
        // by the caller-owned snapshot instead.
        _initialized = true;

        _previousBatchResult = _lastBatchResult;
        _lastBatchResult = null;
        _submittedSnapshot = null;
        Items.Clear();
        ProgressRatio = 0;
        ProgressSummary = "尚未开始";
        ClearError();
        NoticeMessage = null;
        SelectedTask = TaskKinds.First(option => option.Value == kind);

        await AppendAsync(inputs, [], cancellationToken);
        foreach (var item in Items) item.SetPlan(string.Empty);
        NotifyCollectionChanged();
        NotifyResult();
    }

    public void ApplyCompressionDraft(
        CompressionMode mode,
        decimal quality,
        bool removeMetadata,
        OutputPolicy outputPolicy)
    {
        SelectedTask = TaskKinds.First(option => option.Value == BatchTaskKind.Compress);
        SelectedCompressionMode = CompressionModes.First(option => option.Value == mode);
        CustomQuality = quality;
        RemoveMetadata = removeMetadata;
        Output.Apply(outputPolicy);
        NotifyDraft();
    }

    public void ApplyConversionDraft(
        OutputImageFormat format,
        decimal quality,
        string backgroundHex,
        bool removeMetadata,
        OutputPolicy outputPolicy)
    {
        SelectedTask = TaskKinds.First(option => option.Value == BatchTaskKind.Convert);
        SelectedFormat = ConversionFormats.First(option => option.Value == format);
        ConversionQuality = quality;
        BackgroundHex = backgroundHex;
        RemoveMetadata = removeMetadata;
        Output.Apply(outputPolicy);
        NotifyDraft();
    }

    public void ApplyResizeDraft(
        ResizeDraftMode mode,
        decimal? width,
        decimal? height,
        PixelDimensionAnchor anchor,
        bool maintainAspectRatio,
        bool preventUpscaling,
        decimal percentage,
        SameFormatEncodingPolicy encodingPolicy,
        OutputPolicy outputPolicy)
    {
        SelectedTask = TaskKinds.First(option => option.Value == BatchTaskKind.Resize);
        SelectedResizeMode = ResizeModes.First(option => option.Value == mode);
        SetProperty(ref _pixelWidth, width, nameof(PixelWidth));
        SetProperty(ref _pixelHeight, height, nameof(PixelHeight));
        _pixelAnchor = anchor;
        OnPropertyChanged(nameof(PixelAnchor));
        SetProperty(ref _maintainAspectRatio, maintainAspectRatio, nameof(MaintainAspectRatio));
        SetProperty(ref _preventUpscaling, preventUpscaling, nameof(PreventUpscaling));
        SetProperty(ref _percentage, percentage, nameof(Percentage));
        _sameFormatEncoding = encodingPolicy;
        Output.Apply(outputPolicy);
        NotifyDraft();
    }

    public void Dispose()
    {
        _executionCancellation?.Cancel();
        _executionCancellation?.Dispose();
    }

    public void RemoveInputFromView(BatchItemViewModel item)
    {
        if (RemoveInputCommand.CanExecute(item)) RemoveInputCommand.Execute(item);
    }

    private async Task AddFilesAsync(CancellationToken cancellationToken)
    {
        var selection = await _picker.PickImagesAsync(cancellationToken);
        if (selection.Status == DesktopSelectionStatus.Canceled) return;
        if (selection.Status != DesktopSelectionStatus.Selected) { SetPlainError(DesktopErrorText.FromPicker(selection.ErrorMessage)); return; }
        await AppendAsync(selection.Paths.Select(path => new LocalPath(path)).ToArray(), [], cancellationToken);
    }

    private async Task AddFolderAsync(CancellationToken cancellationToken)
    {
        var selection = await _picker.PickFolderAsync(cancellationToken);
        if (selection.Status == DesktopSelectionStatus.Canceled) return;
        if (selection.Status != DesktopSelectionStatus.Selected || selection.Paths.Count != 1) { SetPlainError(DesktopErrorText.FromPicker(selection.ErrorMessage)); return; }
        await AppendAsync([], [new LocalPath(selection.Paths[0])], cancellationToken);
    }

    private async Task AppendAsync(IReadOnlyList<LocalPath> files, IReadOnlyList<LocalPath> directories, CancellationToken cancellationToken)
    {
        IsAppending = true;
        ClearError();
        NoticeMessage = null;
        try
        {
            var existing = Items.Select(item => item.Path).ToArray();
            var result = await _appendInputs.ExecuteAsync(new AppendBatchInputsRequest(existing, files, directories), cancellationToken);
            if (!result.Succeeded) { SetWorkflowError(result.Error); return; }
            var existingViews = Items.ToDictionary(item => item.Path.Value, StringComparerForPaths());
            Items.Clear();
            foreach (var path in result.Value!.InputPaths)
            {
                Items.Add(existingViews.TryGetValue(path.Value, out var existingView) ? existingView : new BatchItemViewModel(path));
            }
            NoticeMessage = $"新增 {result.Value.AddedCount} 张；重复 {result.Value.DuplicateCount}，不支持 {result.Value.UnsupportedCount}，不可读取 {result.Value.UnreadableCount}。";
            NotifyCollectionChanged();
            await ProbeMissingItemsAsync(cancellationToken);
        }
        finally { IsAppending = false; }
    }

    private async Task ProbeMissingItemsAsync(CancellationToken cancellationToken)
    {
        foreach (var item in Items.Where(item => item.Probe is null).ToArray())
        {
            var result = await _openImage.ExecuteAsync(new OpenImageRequest(item.Path), cancellationToken);
            if (result.Succeeded) item.SetProbe(result.Value!.ProbeResult);
            else item.SetProbeError(result.Error!);
        }
        NotifyCollectionChanged();
    }

    private void RemoveInput(BatchItemViewModel item)
    {
        Items.Remove(item);
        NotifyCollectionChanged();
    }

    private async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!TryBuildSubmission(out var submission, out var submissionError))
        {
            SetPlainError(submissionError ?? "当前批量处理配置无效。");
            return;
        }
        if (!_navigation.TryBeginForegroundTask()) { ErrorMessage = "已有任务正在运行，请等待其结束。"; return; }
        using var executionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _executionCancellation = executionCancellation;
        var generation = ++_executionGeneration;
        _acceptedBatchId = null;
        _lastSequence = 0;
        _lastBatchResult = null;
        _submittedSnapshot = submission;
        ClearError();
        NoticeMessage = null;
        IsCanceling = false;
        IsProcessing = true;
        ProgressRatio = 0;
        ProgressSummary = $"准备 {submission!.Inputs.Count} 张图片";
        foreach (var item in Items) item.SetPlan(string.Empty);
        try
        {
            switch (submission.Kind)
            {
                case BatchTaskKind.Compress:
                    await ExecuteCompressAsync(submission, generation, executionCancellation.Token);
                    break;
                case BatchTaskKind.Convert:
                    await ExecuteConvertAsync(submission, generation, executionCancellation.Token);
                    break;
                case BatchTaskKind.Resize:
                    await ExecuteResizeAsync(submission, generation, executionCancellation.Token);
                    break;
            }
        }
        finally
        {
            IsProcessing = false;
            IsCanceling = false;
            _executionCancellation = null;
            _navigation.EndForegroundTask();
            NotifyResult();
        }
    }

    private async Task ExecuteCompressAsync(SubmittedBatchSnapshot submission, long generation, CancellationToken cancellationToken)
    {
        var progress = new CallbackProgress<BatchExecutionProgress<BatchCompressItemResult>>(message =>
            _dispatcher.Post(() => ApplyProgress(generation, message, item => item.JobResult)));
        var result = await _batchCompress.ExecuteAsync(
            new BatchCompressRequest(submission.Inputs, (CompressionProfile)submission.ProcessingProfile, submission.OutputPolicy),
            progress,
            cancellationToken);
        if (!result.Succeeded) { HandleStartRejected(result.Error); return; }
        ApplyFinalResult(generation, result.Value!.BatchResult);
    }

    private async Task ExecuteConvertAsync(SubmittedBatchSnapshot submission, long generation, CancellationToken cancellationToken)
    {
        var progress = new CallbackProgress<BatchExecutionProgress<BatchConvertItemResult>>(message =>
            _dispatcher.Post(() => ApplyProgress(generation, message, item => item.JobResult)));
        var result = await _batchConvert.ExecuteAsync(
            new BatchConvertRequest(submission.Inputs, (ConversionProfile)submission.ProcessingProfile, submission.OutputPolicy),
            progress,
            cancellationToken);
        if (!result.Succeeded) { HandleStartRejected(result.Error); return; }
        ApplyFinalResult(generation, result.Value!.BatchResult);
    }

    private async Task ExecuteResizeAsync(SubmittedBatchSnapshot submission, long generation, CancellationToken cancellationToken)
    {
        var progress = new CallbackProgress<BatchExecutionProgress<BatchResizeItemResult>>(message =>
            _dispatcher.Post(() => ApplyProgress(generation, message, item => item.JobResult)));
        var result = await _batchResize.ExecuteAsync(
            new BatchResizeRequest(submission.Inputs, (ResizePolicy)submission.ProcessingProfile, submission.OutputPolicy, _sameFormatEncoding),
            progress,
            cancellationToken);
        if (!result.Succeeded) { HandleStartRejected(result.Error); return; }
        ApplyFinalResult(generation, result.Value!.BatchResult);
    }

    private void ApplyProgress<T>(long generation, BatchExecutionProgress<T> progress, Func<T, ImageJobResult> resultSelector)
        where T : class
    {
        if (generation != _executionGeneration || !IsProcessing) return;
        if (_acceptedBatchId is null) _acceptedBatchId = progress.Summary.BatchId;
        else if (_acceptedBatchId.Value != progress.Summary.BatchId) return;
        if (progress.Sequence <= _lastSequence) return;
        if (progress.OutputPlan is null || progress.OutputPlan.Items.Count != Items.Count) return;
        for (var index = 0; index < Items.Count; index++)
        {
            if (!PathsEqual(Items[index].Path, progress.OutputPlan.Items[index].InputPath)) return;
        }
        _lastSequence = progress.Sequence;
        for (var index = 0; index < Items.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(Items[index].OutputPath)) Items[index].SetPlan(progress.OutputPlan.Items[index].OutputPath.Value);
        }
        if (progress.ChangedItem is { } changed)
        {
            if (changed.Index < 0 || changed.Index >= Items.Count || !PathsEqual(Items[changed.Index].Path, changed.InputPath)) return;
            if (changed.Status == ImageJobStatus.Running) Items[changed.Index].SetRunning();
            else if (changed.Result is not null) Items[changed.Index].SetTerminal(resultSelector(changed.Result));
        }
        ProgressRatio = progress.Summary.CompletionRatio;
        ProgressSummary = $"已完成 {progress.Summary.CompletedCount}/{progress.Summary.TotalCount} · 成功 {progress.Summary.SucceededCount} · 失败 {progress.Summary.FailedCount} · 跳过 {progress.Summary.SkippedCount}";
    }

    private void ApplyFinalResult(long generation, BatchResult result)
    {
        if (generation != _executionGeneration) return;
        _lastSequence = long.MaxValue;
        _lastBatchResult = result;
        foreach (var item in Items)
        {
            var terminal = result.Items.FirstOrDefault(value => PathsEqual(value.InputPath, item.Path));
            if (terminal is null) item.SetNotStarted();
            else item.SetTerminal(terminal);
        }
        ProgressRatio = result.CompletedCount / (double)result.TotalCount;
        ProgressSummary = ResultSummary;
        NotifyResult();
    }

    private void HandleStartRejected(AtomPixError? error)
    {
        SetWorkflowError(error);
        if (error?.Code == AtomPixErrorCode.OutputPathConflictsWithInput)
        {
            _ = HandleBatchSourceConflictAsync(error);
        }
    }

    private async Task HandleBatchSourceConflictAsync(AtomPixError error)
    {
        var count = error.Details is not null && error.Details.TryGetValue("ConflictCount", out var value) ? value : "一部分";
        var autoRename = await _dialogs.ConfirmAsync("无法覆盖原始图片", $"有 {count} 张图片的输出路径与任务输入相同。AtomPix 禁止覆盖任何源图片。", "改为自动重命名", "返回修改", CancellationToken.None);
        if (autoRename)
        {
            Output.SetOverwrite(OverwritePolicy.AutoRename);
            NoticeMessage = "已改为自动重命名，请确认后重新开始。";
        }
    }

    private async Task CancelAsync(CancellationToken cancellationToken)
    {
        var confirmed = await _dialogs.ConfirmAsync("取消批量任务", "已完成的输出会保留，当前项和未开始项将进入可恢复状态。", "确认取消", "继续处理", cancellationToken);
        if (!confirmed) return;
        IsCanceling = true;
        _executionCancellation?.Cancel();
    }

    private void RecoverFailed()
    {
        if (_lastBatchResult is null) return;
        BuildRecoveryDraft(_lastBatchResult.Items.Where(item => item.Status == ImageJobStatus.Failed).Select(item => item.InputPath), false, "已从失败项建立新草稿；尚未重新执行。");
    }

    private void RecoverUnfinished()
    {
        if (_lastBatchResult is null || _submittedSnapshot is null) return;
        var completedSafe = _lastBatchResult.Items.Where(item => item.Status is ImageJobStatus.Succeeded or ImageJobStatus.Skipped).Select(item => item.InputPath).ToArray();
        BuildRecoveryDraft(_submittedSnapshot.Inputs.Where(path => !completedSafe.Any(done => PathsEqual(done, path))), false, "已从未完成项建立新草稿；尚未重新执行。");
    }

    private void RecoverSkipped()
    {
        if (_lastBatchResult is null) return;
        BuildRecoveryDraft(_lastBatchResult.Items.Where(item => item.Status == ImageJobStatus.Skipped).Select(item => item.InputPath), true, "已从跳过项建立自动重命名草稿；尚未重新执行。");
    }

    private void BuildRecoveryDraft(IEnumerable<LocalPath> paths, bool autoRename, string notice)
    {
        var selected = paths.ToArray();
        if (selected.Length == 0) return;
        var submitted = _submittedSnapshot;
        _previousBatchResult = _lastBatchResult;
        _lastBatchResult = null;
        if (submitted is not null) RestoreSubmittedDraft(submitted);
        var existing = Items.ToDictionary(item => item.Path.Value, StringComparerForPaths());
        Items.Clear();
        foreach (var path in selected)
        {
            var replacement = new BatchItemViewModel(path);
            if (existing.TryGetValue(path.Value, out var oldItem)) replacement.SetProbe(oldItem.Probe);
            Items.Add(replacement);
        }
        if (autoRename) Output.SetOverwrite(OverwritePolicy.AutoRename);
        ProgressRatio = 0;
        ProgressSummary = "尚未开始";
        NoticeMessage = notice;
        ClearError();
        NotifyCollectionChanged();
        NotifyResult();
        RecoveryDraftCreated?.Invoke(this, selected);
        _ = ProbeMissingItemsAsync(CancellationToken.None);
    }

    private void RestoreSubmittedDraft(SubmittedBatchSnapshot submitted)
    {
        _selectedTask = TaskKinds.First(option => option.Value == submitted.Kind);
        Output.Apply(submitted.OutputPolicy);
        switch (submitted.ProcessingProfile)
        {
            case CompressionProfile compression:
                _selectedCompressionMode = CompressionModes.First(option => option.Value == compression.Mode);
                _customQuality = compression.Quality?.Value ?? _customQuality;
                _removeMetadata = compression.MetadataPolicy == MetadataPolicy.Remove;
                break;

            case ConversionProfile conversion:
                _selectedFormat = ConversionFormats.First(option => option.Value == conversion.OutputFormat);
                _conversionQuality = conversion.Quality?.Value ?? _conversionQuality;
                _backgroundHex = conversion.TransparencyPolicy.OpaqueBackgroundColor.ToHexString();
                _removeMetadata = conversion.MetadataPolicy == MetadataPolicy.Remove;
                break;

            case PixelResizePolicy pixel:
                _selectedResizeMode = ResizeModes.First(option => option.Value == ResizeDraftMode.Pixel);
                _pixelWidth = pixel.Width;
                _pixelHeight = pixel.Height;
                _pixelAnchor = pixel.Width is not null ? PixelDimensionAnchor.Width : PixelDimensionAnchor.Height;
                _maintainAspectRatio = pixel.MaintainAspectRatio;
                _preventUpscaling = pixel.PreventUpscaling;
                break;

            case PercentageResizePolicy percentage:
                _selectedResizeMode = ResizeModes.First(option => option.Value == ResizeDraftMode.Percentage);
                _percentage = percentage.Percentage;
                break;
        }
        OnPropertyChanged(string.Empty);
        NotifyDraft();
    }

    private void ResetToEmptyDraft()
    {
        _previousBatchResult = _lastBatchResult;
        _lastBatchResult = null;
        Items.Clear();
        ProgressRatio = 0;
        ProgressSummary = "尚未开始";
        ClearError();
        NoticeMessage = null;
        NotifyCollectionChanged();
        NotifyResult();
    }

    private async Task OpenOutputAsync(CancellationToken cancellationToken)
    {
        if (!HasAvailableOutput)
        {
            SetPlainError("输出文件已不存在，无法打开输出目录。");
            NotifyResult();
            return;
        }
        if (!await _launcher.OpenDirectoryAsync(OutputDirectory, cancellationToken)) SetPlainError("无法打开输出目录，目录可能已被移动。");
    }

    private async Task OpenInputDirectoryAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(InputDirectory) || !await _launcher.OpenDirectoryAsync(InputDirectory, cancellationToken))
        {
            SetPlainError("无法打开输入目录，目录可能已被移动。");
        }
    }

    private async Task ViewItemDetailsAsync(BatchItemViewModel item, CancellationToken cancellationToken)
    {
        var lines = new List<string>
        {
            $"输入：{item.FullPath}",
            $"状态：{item.StatusText}",
            $"图片：{item.ProbeSummary}"
        };
        if (!string.IsNullOrWhiteSpace(item.OutputPath)) lines.Add($"输出：{item.OutputPath}");
        if (!string.IsNullOrWhiteSpace(item.ErrorText)) lines.Add($"原因：{item.ErrorText}");
        if (item.HasDiagnosticId) lines.Add($"诊断编号：{item.DiagnosticId}");
        await _dialogs.ShowInformationAsync("批量项目详情", string.Join(Environment.NewLine, lines), cancellationToken);
    }

    private async Task RelocateInputAsync(BatchItemViewModel item, CancellationToken cancellationToken)
    {
        var selection = await _picker.PickSingleImageAsync(cancellationToken);
        if (selection.Status == DesktopSelectionStatus.Canceled) return;
        if (selection.Status != DesktopSelectionStatus.Selected || selection.Paths.Count != 1)
        {
            SetPlainError(DesktopErrorText.FromPicker(selection.ErrorMessage));
            return;
        }

        var replacement = new LocalPath(selection.Paths[0]);
        var opened = await _openImage.ExecuteAsync(new OpenImageRequest(replacement), cancellationToken);
        if (!opened.Succeeded)
        {
            SetWorkflowError(opened.Error);
            return;
        }

        if (_lastBatchResult is not null)
        {
            var submitted = _submittedSnapshot;
            _previousBatchResult = _lastBatchResult;
            _lastBatchResult = null;
            if (submitted is not null) RestoreSubmittedDraft(submitted);
            Items.Clear();
            var replacementItem = new BatchItemViewModel(replacement);
            replacementItem.SetProbe(opened.Value!.ProbeResult);
            Items.Add(replacementItem);
            ProgressRatio = 0;
            ProgressSummary = "尚未开始";
            NoticeMessage = "已重新定位并建立新草稿；原任务结果保持只读，尚未重新执行。";
            ClearError();
            NotifyCollectionChanged();
            NotifyResult();
            return;
        }

        item.ReplacePath(replacement, opened.Value!.ProbeResult);
        NoticeMessage = "已更新输入路径，请确认任务设置后开始。";
        ClearError();
        NotifyCollectionChanged();
    }

    private Task CopyItemDiagnosticIdAsync(BatchItemViewModel item, CancellationToken cancellationToken) =>
        item.DiagnosticId is { } value ? _clipboard.SetTextAsync(value, cancellationToken) : Task.CompletedTask;

    private Task CopyDiagnosticIdAsync(CancellationToken cancellationToken) =>
        DiagnosticId is { } value ? _clipboard.SetTextAsync(value, cancellationToken) : Task.CompletedTask;

    private bool TryBuildSubmission(out SubmittedBatchSnapshot? snapshot, out string? error)
    {
        snapshot = null;
        error = null;
        if (Items.Count == 0) { error = "请至少添加一张图片。"; return false; }
        if (!Output.TryBuild(out var outputDraft, out error)) return false;
        OutputNamingPolicy naming;
        try { naming = new OutputNamingPolicy(OutputNamingMode.CustomPattern, null, EffectivePattern); }
        catch (ArgumentException) { error = "文件名格式无效，只能使用 {name} 与 {index} 占位符。"; return false; }
        var output = new OutputPolicy(outputDraft!.LocationPolicy, naming, outputDraft.OverwritePolicy);
        object profile;
        try
        {
            profile = SelectedTask.Value switch
            {
                BatchTaskKind.Compress => BuildCompressionProfile(),
                BatchTaskKind.Convert => BuildConversionProfile(),
                BatchTaskKind.Resize => BuildResizePolicy(),
                _ => throw new ArgumentOutOfRangeException()
            };
        }
        catch (ArgumentException)
        {
            error = SelectedTask.Value switch
            {
                BatchTaskKind.Compress => "自定义质量必须是 1 到 100 的整数。",
                BatchTaskKind.Convert => "转换质量必须是 1 到 100 的整数，背景色必须使用 #RRGGBB。",
                _ => "Resize 参数无效；保持比例时至少填写一边，关闭时必须同时填写宽高。"
            };
            return false;
        }
        snapshot = new SubmittedBatchSnapshot(SelectedTask.Value, Items.Select(item => item.Path).ToArray(), profile, output);
        return true;
    }

    private CompressionProfile BuildCompressionProfile()
    {
        var mode = SelectedCompressionMode.Value;
        ImageQuality? quality = mode switch
        {
            CompressionMode.Smart => null,
            CompressionMode.HighQuality => new ImageQuality(90),
            CompressionMode.Balanced => new ImageQuality(80),
            CompressionMode.Maximum => new ImageQuality(65),
            CompressionMode.Custom => new ImageQuality(ToQuality(CustomQuality)),
            _ => throw new ArgumentOutOfRangeException()
        };
        return new CompressionProfile(mode, quality, RemoveMetadata ? MetadataPolicy.Remove : MetadataPolicy.Preserve);
    }

    private ConversionProfile BuildConversionProfile()
    {
        ImageQuality? quality = IsLossyConversion ? new ImageQuality(ToQuality(ConversionQuality)) : null;
        if (!RgbColor.TryParse(BackgroundHex, out var color)) throw new ArgumentException("Invalid color.");
        return new ConversionProfile(SelectedFormat.Value, quality, RemoveMetadata ? MetadataPolicy.Remove : MetadataPolicy.Preserve, new TransparencyPolicy(color));
    }

    private ResizePolicy BuildResizePolicy() => SelectedResizeMode.Value switch
    {
        ResizeDraftMode.Pixel => new PixelResizePolicy(
            MaintainAspectRatio && PixelAnchor == PixelDimensionAnchor.Height ? null : ToOptionalPositiveInteger(PixelWidth),
            MaintainAspectRatio && PixelAnchor == PixelDimensionAnchor.Width ? null : ToOptionalPositiveInteger(PixelHeight),
            MaintainAspectRatio,
            PreventUpscaling),
        ResizeDraftMode.Percentage => new PercentageResizePolicy(Percentage),
        _ => throw new ArgumentOutOfRangeException()
    };

    private void NotifyCollectionChanged()
    {
        foreach (var item in Items) item.SetCanRemove(CanEditInputs);
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasInputs));
        OnPropertyChanged(nameof(InputCollectionSummary));
        OnPropertyChanged(nameof(TransparentItemCount));
        OnPropertyChanged(nameof(LossyItemCount));
        OnPropertyChanged(nameof(InputDirectory));
        NotifyDraft();
        RemoveInputCommand.NotifyCanExecuteChanged();
        OpenInputDirectoryCommand.NotifyCanExecuteChanged();
    }

    private void NotifyDraft()
    {
        RecalculateResizeEstimates();
        OnPropertyChanged(nameof(IsCompressTask));
        OnPropertyChanged(nameof(IsConvertTask));
        OnPropertyChanged(nameof(IsResizeTask));
        OnPropertyChanged(nameof(IsCustomCompression));
        OnPropertyChanged(nameof(IsLossyConversion));
        OnPropertyChanged(nameof(IsPixelResize));
        OnPropertyChanged(nameof(IsPercentageResize));
        OnPropertyChanged(nameof(ShowBatchTransparency));
        OnPropertyChanged(nameof(DraftError));
        OnPropertyChanged(nameof(HasDraftError));
        OnPropertyChanged(nameof(CanAttemptStart));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(EffectivePattern));
        OnPropertyChanged(nameof(PatternWillAppendIndex));
        OnPropertyChanged(nameof(OutputExamples));
        OnPropertyChanged(nameof(FileNamePattern));
        StartCommand.NotifyCanExecuteChanged();
        InsertNameTokenCommand.NotifyCanExecuteChanged();
        InsertIndexTokenCommand.NotifyCanExecuteChanged();
    }

    private void NotifyState()
    {
        foreach (var item in Items) item.SetCanRemove(CanEditInputs);
        OnPropertyChanged(nameof(CanEditInputs));
        OnPropertyChanged(nameof(CanEditDraft));
        OnPropertyChanged(nameof(CanAttemptStart));
        OnPropertyChanged(nameof(CanStart));
        AddFilesCommand.NotifyCanExecuteChanged();
        AddFolderCommand.NotifyCanExecuteChanged();
        RemoveInputCommand.NotifyCanExecuteChanged();
        StartCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        RetryFailedCommand.NotifyCanExecuteChanged();
        ProcessUnfinishedCommand.NotifyCanExecuteChanged();
        ProcessSkippedWithAutoRenameCommand.NotifyCanExecuteChanged();
        ContinueOtherCommand.NotifyCanExecuteChanged();
        RelocateInputCommand.NotifyCanExecuteChanged();
    }

    private void NotifyResult()
    {
        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(HasPreviousResult));
        OnPropertyChanged(nameof(PreviousResultSummary));
        OnPropertyChanged(nameof(ResultTitle));
        OnPropertyChanged(nameof(ResultSummary));
        OnPropertyChanged(nameof(ResultSizeChange));
        OnPropertyChanged(nameof(HasFailedItems));
        OnPropertyChanged(nameof(HasSkippedItems));
        OnPropertyChanged(nameof(HasUnfinishedItems));
        OnPropertyChanged(nameof(OutputDirectory));
        OnPropertyChanged(nameof(HasAvailableOutput));
        OnPropertyChanged(nameof(HasMissingOutput));
        NotifyState();
        RetryFailedCommand.NotifyCanExecuteChanged();
        ProcessUnfinishedCommand.NotifyCanExecuteChanged();
        ProcessSkippedWithAutoRenameCommand.NotifyCanExecuteChanged();
        ContinueOtherCommand.NotifyCanExecuteChanged();
        OpenOutputCommand.NotifyCanExecuteChanged();
        ViewItemDetailsCommand.NotifyCanExecuteChanged();
        RelocateInputCommand.NotifyCanExecuteChanged();
        CopyItemDiagnosticIdCommand.NotifyCanExecuteChanged();
    }

    private void SetWorkflowError(AtomPixError? error)
    {
        ErrorMessage = DesktopErrorText.FromWorkflow(error);
        DiagnosticId = DesktopErrorText.DiagnosticId(error);
    }

    private void SetPlainError(string? message)
    {
        ErrorMessage = message;
        DiagnosticId = null;
    }

    private void ClearError()
    {
        ErrorMessage = null;
        DiagnosticId = null;
    }

    private void RecalculateResizeEstimates()
    {
        if (!IsResizeTask) return;
        ResizePolicy? policy;
        try { policy = BuildResizePolicy(); }
        catch (ArgumentException) { policy = null; }
        foreach (var item in Items)
        {
            if (policy is null || item.Probe is null) { item.SetEstimatedSize("—"); continue; }
            try
            {
                var size = policy.Resolve(new ImageSize(item.Probe.Width, item.Probe.Height));
                item.SetEstimatedSize($"{size.Width} × {size.Height}");
            }
            catch (ArgumentException) { item.SetEstimatedSize("不可用"); }
        }
    }

    private string BuildOutputExamples()
    {
        if (Items.Count == 0) return "添加图片后显示实际示例";
        string effective;
        try
        {
            _ = new OutputNamingPolicy(OutputNamingMode.CustomPattern, null, FileNamePattern);
            effective = EffectivePattern;
        }
        catch (ArgumentException) { return "—"; }
        var width = Math.Max(3, Items.Count.ToString(System.Globalization.CultureInfo.InvariantCulture).Length);
        return string.Join("  ·  ", Items.Take(3).Select((item, index) =>
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(item.Path.Value);
            var extension = SelectedTask.Value == BatchTaskKind.Convert ? SelectedFormat.Value switch
            {
                OutputImageFormat.Jpeg => ".jpg",
                OutputImageFormat.Png => ".png",
                _ => ".webp"
            } : System.IO.Path.GetExtension(item.Path.Value);
            return effective.Replace("{name}", name, StringComparison.Ordinal).Replace("{index}", (index + 1).ToString($"D{width}"), StringComparison.Ordinal) + extension;
        }));
    }

    private static int ToQuality(decimal value)
    {
        if (value is < 1 or > 100 || value != decimal.Truncate(value)) throw new ArgumentOutOfRangeException(nameof(value));
        return decimal.ToInt32(value);
    }
    private static int? ToOptionalPositiveInteger(decimal? value)
    {
        if (value is null) return null;
        if (value <= 0 || value > int.MaxValue || value != decimal.Truncate(value.Value)) throw new ArgumentOutOfRangeException(nameof(value));
        return decimal.ToInt32(value.Value);
    }
    private static string FormatBatchStatus(BatchJobStatus status) => status switch
    {
        BatchJobStatus.Succeeded => "批量任务完成",
        BatchJobStatus.PartiallySucceeded => "批量任务部分完成",
        BatchJobStatus.Failed => "批量任务失败",
        BatchJobStatus.Canceled => "批量任务已取消",
        _ => status.ToString()
    };
    private static string FormatBatchSizeChange(BatchResult? result)
    {
        if (result?.TotalSizeChangeKind is null || result.TotalSizeDeltaBytes is null) return result is null ? string.Empty : "暂无可比较结果";
        var bytes = Math.Abs(result.TotalSizeDeltaBytes.Value);
        var ratio = Math.Abs(result.TotalSizeDeltaRatio ?? 0) * 100;
        return result.TotalSizeChangeKind switch
        {
            FileSizeChangeKind.Reduced => $"总体积减少 {DesktopResultText.FormatBytes(bytes)}（{ratio:0.##}%）",
            FileSizeChangeKind.Unchanged => "总体积未变化",
            FileSizeChangeKind.Increased => $"总体积增加 {DesktopResultText.FormatBytes(bytes)}（{ratio:0.##}%）",
            _ => "暂无可比较结果"
        };
    }
    private static StringComparer StringComparerForPaths() => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static bool PathsEqual(LocalPath left, LocalPath right) => StringComparerForPaths().Equals(System.IO.Path.GetFullPath(left.Value), System.IO.Path.GetFullPath(right.Value));

    private sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }
}
