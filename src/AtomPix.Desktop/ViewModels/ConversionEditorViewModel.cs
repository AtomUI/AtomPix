namespace AtomPix.Desktop.ViewModels;

using AtomPix.Core.Compression;
using AtomPix.Core.Conversion;
using AtomPix.Core.Errors;
using AtomPix.Core.Jobs;
using AtomPix.Core.Output;
using AtomPix.Core.ValueObjects;
using AtomPix.Desktop.Localization;
using AtomPix.Desktop.Navigation;
using AtomPix.Desktop.Platform;
using AtomPix.Imaging.Abstractions.Processing;
using AtomPix.Workflows.Images;
using AtomPix.Workflows.Settings;
using AtomUI.City.Presentation;

public sealed class ConversionEditorViewModel : AtomPixViewModelBase, IToolEditorActions, IResultAvailabilityAware, IOperationResultFeedbackSource
{
    private readonly IDesktopPickerService _picker;
    private readonly IDesktopLauncherService _launcher;
    private readonly IDesktopDialogService _dialogs;
    private readonly IDesktopClipboardService _clipboard;
    private readonly ResultOutputGuard _outputGuard;
    private readonly OpenImageWorkflow _openImage;
    private readonly LoadSettingsWorkflow _loadSettings;
    private readonly ConvertImageWorkflow _convert;
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
        _convert = convert ?? throw new ArgumentNullException(nameof(convert));
        _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        _foregroundTasks = foregroundTasks ?? throw new ArgumentNullException(nameof(foregroundTasks));
        _text = text ?? throw new ArgumentNullException(nameof(text));
        ResultFeedback = new OperationResultFeedback(_text);
        var commands = commandFactory ?? HeadlessUiCommandFactory.Instance;
        var activationOwner = commandFactory is null ? null : this;
        Output = new OutputPolicyEditorViewModel(_picker, _text, OutputDraftChanged, ResultFeedback.ShowWarning, commands);

        Formats =
        [
            new DesktopChoiceOption<OutputImageFormat>("JPEG", OutputImageFormat.Jpeg),
            new DesktopChoiceOption<OutputImageFormat>("PNG", OutputImageFormat.Png),
            new DesktopChoiceOption<OutputImageFormat>("WebP", OutputImageFormat.WebP)
        ];
        _selectedFormat = Formats[2];
        SelectImageCommand = commands.CreateAsync(SelectImageAsync, () => !IsProcessing, activationOwner: activationOwner);
        StartCommand = commands.CreateAsync(StartAsync, () => IsContentReady && !IsProcessing, activationOwner: activationOwner);
        CancelCommand = commands.Create(() => _executionCancellation?.Cancel(), () => IsProcessing);
        UseWhiteCommand = commands.Create(() => BackgroundHex = "#FFFFFF", () => !IsProcessing);
        UseBlackCommand = commands.Create(() => BackgroundHex = "#000000", () => !IsProcessing);
        OpenOutputCommand = commands.CreateAsync(OpenOutputAsync, () => IsSuccess, activationOwner: activationOwner);
        ContinueResizeCommand = commands.Create(ContinueResize, () => IsSuccess);
        CopyDiagnosticIdCommand = commands.CreateAsync(CopyDiagnosticIdAsync, () => HasDiagnosticId, activationOwner: activationOwner);
    }

    public IReadOnlyList<DesktopChoiceOption<OutputImageFormat>> Formats { get; }
    public OutputPolicyEditorViewModel Output { get; }
    public OperationResultFeedback ResultFeedback { get; }
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

    public string StartActionLabel => _text.GetString("Tools.Action.StartSingle");

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
        ? _text.GetString("Tools.Conversion.Transparency.NoneDetected")
        : SelectedFormat.Value == OutputImageFormat.Jpeg
            ? _text.Format("Tools.Conversion.Transparency.WillFlatten", NormalizedBackgroundHex)
            : _text.GetString("Tools.Conversion.Transparency.WillPreserve");
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
        ImageJobStatus.Succeeded => _text.GetString("Tools.Conversion.Status.Succeeded"),
        ImageJobStatus.Canceled => _text.GetString("Common.Status.TaskCanceled"),
        ImageJobStatus.Skipped => _text.GetString("Common.Status.SkippedExisting"),
        ImageJobStatus.Failed => _text.GetString("Tools.Conversion.Status.Failed"),
        _ => string.Empty
    };
    public string ResultOutputPath => _lastResult?.OutputPath?.Value ?? string.Empty;
    public bool IsResultOutputAvailable => IsSuccess && _outputGuard.FileExists(_lastResult?.OutputPath);
    public bool IsResultOutputMissing => IsSuccess && _lastResult?.OutputPath is not null && !IsResultOutputAvailable;
    public string ResultSizeChange => DesktopResultText.FormatSizeChange(_lastResult, _text);
    public string ResultTransparency => _transparencyResult?.Outcome switch
    {
        TransparencyOutcome.NotPresent => _text.GetString("Tools.Conversion.Transparency.NotPresent"),
        TransparencyOutcome.Preserved => _text.GetString("Tools.Conversion.Transparency.Preserved"),
        TransparencyOutcome.Flattened => _text.Format("Tools.Conversion.Transparency.Flattened", _transparencyResult.BackgroundColor!.ToHexString()),
        _ => string.Empty
    };

    public IAsyncRelayCommand SelectImageCommand { get; }
    public IAsyncRelayCommand StartCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public IRelayCommand UseWhiteCommand { get; }
    public IRelayCommand UseBlackCommand { get; }
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

    protected override void OnDisposed()
    {
        CancelLoad();
        _executionCancellation?.Cancel();
        _executionCancellation?.Dispose();
    }

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
        if (!TryBuildProfile(out var profile, out var profileError))
        {
            ResultFeedback.ShowWarning(profileError ?? _text.GetString("Tools.Conversion.Validation.InvalidProfile"));
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
            else ResultFeedback.Show(_lastResult, result.Value.OutputDisposition, _text.GetString("Tools.Conversion.Status.Succeeded"), ResultSizeChange);
            NotifyResult();
        }
        finally
        {
            _executionCancellation = null;
            _foregroundTasks.EndForegroundTask();
        }
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
        if (_lastResult?.OutputPath is not { } output) return;
        _ = ContinueResizeAsync(output);
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

    private async Task CopyDiagnosticIdAsync(CancellationToken cancellationToken)
    {
        if (DiagnosticId is not null) await _clipboard.SetTextAsync(DiagnosticId, cancellationToken);
    }

    private bool TryBuildProfile(out ConversionProfile? profile, out string? error)
    {
        profile = null;
        error = null;
        if (_probe is null) { error = _text.GetString("Common.Validation.SelectImageFirst"); return false; }
        if (_probe.IsAnimated || _probe.FrameCount != 1) { error = _text.GetString("Tools.Conversion.Validation.AnimatedUnsupported"); return false; }
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
            error = !RgbColor.TryParse(BackgroundHex, out _)
                ? _text.GetString("Tools.Conversion.Validation.BackgroundColor")
                : _text.GetString("Tools.Conversion.Validation.Quality");
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

    private void SetError(AtomPixError? error) { ErrorMessage = DesktopErrorText.FromWorkflow(error, _text); DiagnosticId = DesktopErrorText.DiagnosticId(error); }
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
