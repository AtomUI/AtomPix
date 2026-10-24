namespace AtomPix.Desktop.ViewModels;

using AtomPix.Core.Crop;
using AtomPix.Core.Errors;
using AtomPix.Core.Jobs;
using AtomPix.Core.Output;
using AtomPix.Core.Resize;
using AtomPix.Core.ValueObjects;
using AtomPix.Desktop.Controls;
using AtomPix.Desktop.Localization;
using AtomPix.Desktop.Navigation;
using AtomPix.Desktop.Platform;
using AtomPix.Imaging.Abstractions.Formats;
using AtomPix.Imaging.Abstractions.Processing;
using AtomPix.Workflows.Images;
using AtomPix.Workflows.Settings;
using AtomUI.City.Presentation;

public sealed record CropRatioOption(string Label, int? WidthUnits, int? HeightUnits)
{
    public double Ratio => WidthUnits is null || HeightUnits is null ? 0 : WidthUnits.Value / (double)HeightUnits.Value;
    public override string ToString() => Label;
}

public sealed class CropEditorViewModel : AtomPixViewModelBase, IDesktopForegroundTask, IResultAvailabilityAware, IOperationResultFeedbackSource
{
    private readonly IDesktopPickerService _picker;
    private readonly IDesktopLauncherService _launcher;
    private readonly IDesktopDialogService _dialogs;
    private readonly IDesktopClipboardService _clipboard;
    private readonly ResultOutputGuard _outputGuard;
    private readonly OpenImageWorkflow _openImage;
    private readonly LoadSettingsWorkflow _loadSettings;
    private readonly CropImageWorkflow _crop;
    private readonly IDesktopNavigator _navigator;
    private readonly IDesktopForegroundTaskGate _foregroundTasks;
    private readonly IAtomPixLocalizedText _text;
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
        IDesktopNavigator navigator,
        IDesktopForegroundTaskGate foregroundTasks,
        IAtomPixLocalizedText text,
        IUiCommandFactory? commandFactory = null)
    {
        _picker = picker ?? throw new ArgumentNullException(nameof(picker));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _outputGuard = outputGuard ?? throw new ArgumentNullException(nameof(outputGuard));
        _openImage = openImage ?? throw new ArgumentNullException(nameof(openImage));
        _loadSettings = loadSettings ?? throw new ArgumentNullException(nameof(loadSettings));
        _crop = crop ?? throw new ArgumentNullException(nameof(crop));
        _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        _foregroundTasks = foregroundTasks ?? throw new ArgumentNullException(nameof(foregroundTasks));
        _text = text ?? throw new ArgumentNullException(nameof(text));
        ResultFeedback = new OperationResultFeedback(_text);
        var commands = commandFactory ?? HeadlessUiCommandFactory.Instance;
        var activationOwner = commandFactory is null ? null : this;
        Output = new OutputPolicyEditorViewModel(_picker, _text, OutputDraftChanged, ResultFeedback.ShowWarning, commands);

        Ratios =
        [
            new CropRatioOption(_text.GetString("Common.Option.Crop.Custom"), null, null),
            new CropRatioOption("3:2", 3, 2),
            new CropRatioOption("4:3", 4, 3),
            new CropRatioOption("5:4", 5, 4),
            new CropRatioOption("1:1", 1, 1)
        ];
        _selectedRatio = Ratios[0];
        SelectImageCommand = commands.CreateAsync(SelectImageAsync, () => !IsProcessing, activationOwner: activationOwner);
        StartCommand = commands.CreateAsync(StartAsync, () => IsContentReady && !IsProcessing, activationOwner: activationOwner);
        CancelCommand = commands.Create(() => _executionCancellation?.Cancel(), () => IsProcessing);
        OpenOutputCommand = commands.CreateAsync(OpenOutputAsync, () => IsSuccess, activationOwner: activationOwner);
        ContinueResizeCommand = commands.Create(ContinueResize, () => IsSuccess);
        CopyDiagnosticIdCommand = commands.CreateAsync(CopyDiagnosticIdAsync, () => HasDiagnosticId, activationOwner: activationOwner);
    }

    public IReadOnlyList<CropRatioOption> Ratios { get; }
    public OutputPolicyEditorViewModel Output { get; }
    public OperationResultFeedback ResultFeedback { get; }
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
    public string EncodingSummary => _text.Format(
        "Tools.Crop.EncodingSummary",
        _encodingPolicy.LossyQuality.Value,
        _text.GetString(_encodingPolicy.MetadataPolicy == AtomPix.Core.Compression.MetadataPolicy.Remove
            ? "Common.Metadata.Remove"
            : "Common.Metadata.Preserve"));
    public SameFormatEncodingPolicy EncodingPolicy => _encodingPolicy;
    public string? ErrorMessage { get => _errorMessage; private set { if (SetProperty(ref _errorMessage, value)) OnPropertyChanged(nameof(HasError)); } }
    public string? DiagnosticId { get => _diagnosticId; private set { if (SetProperty(ref _diagnosticId, value)) { OnPropertyChanged(nameof(HasDiagnosticId)); CopyDiagnosticIdCommand.NotifyCanExecuteChanged(); } } }
    public string ResultTitle => _lastResult?.Status switch
    {
        ImageJobStatus.Succeeded => _text.GetString("Tools.Crop.Status.Succeeded"),
        ImageJobStatus.Canceled => _text.GetString("Common.Status.TaskCanceled"),
        ImageJobStatus.Skipped => _text.GetString("Common.Status.SkippedExisting"),
        ImageJobStatus.Failed => _text.GetString("Tools.Crop.Status.Failed"),
        _ => string.Empty
    };
    public string ResultOutputPath => _lastResult?.OutputPath?.Value ?? string.Empty;
    public bool IsResultOutputAvailable => IsSuccess && _outputGuard.FileExists(_lastResult?.OutputPath);
    public bool IsResultOutputMissing => IsSuccess && _lastResult?.OutputPath is not null && !IsResultOutputAvailable;
    public string ResultDetails => _actualOutputSize is null
        ? string.Empty
        : _text.Format("Tools.Crop.ActualOutput", _actualOutputSize.Value.Width, _actualOutputSize.Value.Height);
    public string ResultSizeChange => DesktopResultText.FormatSizeChange(_lastResult, _text);

    public IAsyncRelayCommand SelectImageCommand { get; }
    public IAsyncRelayCommand StartCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public IAsyncRelayCommand OpenOutputCommand { get; }
    public IRelayCommand ContinueResizeCommand { get; }
    public IAsyncRelayCommand CopyDiagnosticIdCommand { get; }

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

    protected override void OnDisposed() { CancelLoad(); _executionCancellation?.Cancel(); _executionCancellation?.Dispose(); }

    private async Task SelectImageAsync(CancellationToken cancellationToken)
    {
        var selection = await _picker.PickSingleImageAsync(cancellationToken);
        if (selection.Status == DesktopSelectionStatus.Canceled) return;
        if (selection.Status != DesktopSelectionStatus.Selected || selection.Paths.Count != 1) { ErrorMessage = DesktopErrorText.FromPicker(selection.ErrorMessage, _text); return; }
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
            ResultFeedback.ShowWarning(cropError ?? _text.GetString("Tools.Crop.Validation.InvalidSelection"));
            return;
        }
        if (!Output.TryBuild(out var outputPolicy, out var outputError))
        {
            ResultFeedback.ShowWarning(outputError ?? _text.GetString("Common.Validation.Output.Invalid"));
            return;
        }
        if (!_foregroundTasks.TryBeginForegroundTask()) { ErrorMessage = _text.GetString("Common.Error.ForegroundTaskBusy"); return; }
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
            else ResultFeedback.Show(_lastResult, result.Value.OutputDisposition, _text.GetString("Tools.Crop.Status.Succeeded"), ResultDetails);
            NotifyResult();
        }
        finally { _executionCancellation = null; _foregroundTasks.EndForegroundTask(); }
    }

    private async Task HandleSourceConflictAsync(CancellationToken cancellationToken)
    {
        var useAutoRename = await _dialogs.ConfirmAsync(
            _text.GetString("Common.Dialog.SourceOverwrite.Title"),
            _text.GetString("Common.Dialog.SourceOverwrite.Message"),
            _text.GetString("Common.Action.UseAutoRename"),
            _text.GetString("Common.Action.BackToEdit"),
            cancellationToken);
        if (useAutoRename)
        {
            Output.SetOverwrite(OverwritePolicy.AutoRename);
            ErrorMessage = _text.GetString("Common.Message.AutoRenameApplied");
        }
        else ErrorMessage = _text.GetString("Common.Error.SourceOverwriteChangeOutput");
    }

    private async Task OpenOutputAsync(CancellationToken cancellationToken)
    {
        if (!EnsureResultOutputAvailable()) return;
        var directory = Path.GetDirectoryName(ResultOutputPath);
        if (string.IsNullOrWhiteSpace(directory) || !await _launcher.OpenDirectoryAsync(directory, cancellationToken)) ErrorMessage = _text.GetString("Common.Error.OpenOutputDirectoryFailed");
    }

    private void ContinueResize()
    {
        if (!EnsureResultOutputAvailable()) return;
        if (_lastResult?.OutputPath is { } output) _ = ContinueResizeAsync(output);
    }

    private bool EnsureResultOutputAvailable()
    {
        if (IsResultOutputAvailable) return true;
        ErrorMessage = _text.GetString("Common.Error.OutputMissingReprocess");
        NotifyResult();
        return false;
    }
    private async Task ContinueResizeAsync(LocalPath output)
    {
        var opened = await _openImage.ExecuteAsync(new OpenImageRequest(output), CancellationToken.None);
        if (!opened.Succeeded) { SetError(opened.Error); return; }
        _navigator.Navigate(new DesktopNavigationRequest(DesktopRoute.Resize, new SingleImageNavigationContext(output, opened.Value!.ProbeResult)));
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
        if (_probe is null) { error = _text.GetString("Common.Validation.SelectImageFirst"); return false; }
        if (_probe.IsAnimated || _probe.FrameCount != 1 || _probe.Format is not (ImageFormatKind.Jpeg or ImageFormatKind.Png or ImageFormatKind.WebP or ImageFormatKind.Bmp))
        {
            error = _text.GetString("Tools.Crop.Validation.UnsupportedFormat");
            return false;
        }
        try
        {
            rectangle = new CropRectangle(ToNonNegativeInteger(CropX), ToNonNegativeInteger(CropY), ToPositiveInteger(CropWidth), ToPositiveInteger(CropHeight));
            var validation = CropRules.ValidateCropRectangle(new ImageSize(_probe.Width, _probe.Height), rectangle);
            if (!validation.Succeeded) { error = _text.GetString("Tools.Crop.Validation.InsideImage"); rectangle = null; return false; }
            return true;
        }
        catch (ArgumentException)
        {
            error = _text.GetString("Tools.Crop.Validation.IntegerBounds");
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
    private void SetError(AtomPixError? error) { ErrorMessage = DesktopErrorText.FromWorkflow(error, _text); DiagnosticId = DesktopErrorText.DiagnosticId(error); }
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
