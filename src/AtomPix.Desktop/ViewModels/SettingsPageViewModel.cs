namespace AtomPix.Desktop.ViewModels;

using System.Runtime.InteropServices;
using System.Reflection;
using AtomPix.Core.Compression;
using AtomPix.Core.Conversion;
using AtomPix.Core.Output;
using AtomPix.Core.Ports;
using AtomPix.Core.Resize;
using AtomPix.Core.Settings;
using AtomPix.Desktop.Localization;
using AtomPix.Desktop.Platform;
using AtomPix.Workflows.Settings;
using AtomUI.City.Presentation;
using AtomUI.City.State;

public enum SettingsSection
{
    Compression,
    Conversion,
    Output,
    About
}

public sealed class SettingsPageViewModel : AtomPixViewModelBase
{
    private readonly LoadSettingsWorkflow _loadSettings;
    private readonly SaveSettingsWorkflow _saveSettings;
    private readonly IDesktopDialogService _dialogs;
    private readonly IDesktopPickerService _picker;
    private readonly IDesktopLauncherService _launcher;
    private readonly IAppPathProvider _paths;
    private readonly IAtomPixLocalizedText _text;
    private readonly IAtomPixApplicationState? _applicationState;
    private readonly IDesktopDispatcher? _dispatcher;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private CancellationTokenSource? _loadCancellation;
    private IStateSubscription? _settingsSubscription;
    private AppSettings? _originalSettings;
    private bool _isApplying;
    private bool _isLoaded;
    private bool _isLoading;
    private bool _isSaving;
    private bool _isDirty;
    private string? _errorMessage;
    private string? _saveMessage;
    private bool _isSaveMessageSuccess;
    private SettingsSection _selectedSection;
    private DesktopChoiceOption<CompressionMode> _selectedCompressionMode;
    private decimal _customCompressionQuality = 80;
    private DesktopChoiceOption<OutputImageFormat> _selectedConversionFormat;
    private decimal _conversionQuality = 80;
    private string _backgroundHex = "#FFFFFF";
    private bool _removeMetadata = true;
    private ImageQuality _sameFormatQuality = new(90);
    private DesktopChoiceOption<OutputLocationMode> _selectedOutputLocation;
    private string _subfolderName = "AtomPix_Output";
    private string _customOutputDirectory = string.Empty;
    private string _fileNamePattern = "{name}_atompix";
    private DesktopChoiceOption<OverwritePolicy> _selectedOverwritePolicy;
    private DesktopChoiceOption<ThemeMode> _selectedTheme;
    private DesktopChoiceOption<string?> _selectedLanguage;

    public SettingsPageViewModel(
        LoadSettingsWorkflow loadSettings,
        SaveSettingsWorkflow saveSettings,
        IDesktopDialogService dialogs,
        IDesktopPickerService picker,
        IDesktopLauncherService launcher,
        IAppPathProvider paths,
        IDesktopClipboardService clipboard,
        IAtomPixLocalizedText text,
        IUiCommandFactory? commandFactory = null,
        IAtomPixApplicationState? applicationState = null,
        IDesktopDispatcher? dispatcher = null)
    {
        _loadSettings = loadSettings ?? throw new ArgumentNullException(nameof(loadSettings));
        _saveSettings = saveSettings ?? throw new ArgumentNullException(nameof(saveSettings));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _picker = picker ?? throw new ArgumentNullException(nameof(picker));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _applicationState = applicationState;
        _dispatcher = dispatcher;
        var commands = commandFactory ?? HeadlessUiCommandFactory.Instance;
        var activationOwner = commandFactory is null ? null : this;
        Diagnostic = new DiagnosticErrorViewModel(clipboard, commands);

        CompressionModes =
        [
            new(_text.GetString("Common.Option.Compression.Smart"), CompressionMode.Smart),
            new(_text.GetString("Common.Option.Compression.HighQuality"), CompressionMode.HighQuality),
            new(_text.GetString("Common.Option.Compression.Balanced"), CompressionMode.Balanced),
            new(_text.GetString("Common.Option.Compression.Maximum"), CompressionMode.Maximum),
            new(_text.GetString("Common.Option.Compression.Custom"), CompressionMode.Custom)
        ];
        ConversionFormats =
        [
            new("JPEG", OutputImageFormat.Jpeg),
            new("PNG", OutputImageFormat.Png),
            new("WebP", OutputImageFormat.WebP)
        ];
        OutputLocations =
        [
            new(_text.GetString("Common.Option.OutputLocation.SubfolderLong"), OutputLocationMode.Subfolder),
            new(_text.GetString("Common.Option.OutputLocation.SameAsInputLong"), OutputLocationMode.SameAsInput),
            new(_text.GetString("Common.Option.OutputLocation.CustomDirectory"), OutputLocationMode.CustomDirectory)
        ];
        OverwritePolicies =
        [
            new(_text.GetString("Common.Option.Overwrite.AutoRename"), OverwritePolicy.AutoRename),
            new(_text.GetString("Common.Option.Overwrite.Skip"), OverwritePolicy.Skip),
            new(_text.GetString("Common.Option.Overwrite.Overwrite"), OverwritePolicy.Overwrite)
        ];
        Themes =
        [
            new(_text.GetString("Settings.Option.Theme.System"), ThemeMode.System),
            new(_text.GetString("Settings.Option.Theme.Light"), ThemeMode.Light),
            new(_text.GetString("Settings.Option.Theme.Dark"), ThemeMode.Dark)
        ];
        Languages =
        [
            new(_text.GetString("Settings.Option.Language.System"), null),
            new(_text.GetString("Settings.Option.Language.ChineseSimplified"), "zh-CN")
        ];

        _selectedCompressionMode = CompressionModes[0];
        _selectedConversionFormat = ConversionFormats[2];
        _selectedOutputLocation = OutputLocations[0];
        _selectedOverwritePolicy = OverwritePolicies[0];
        _selectedTheme = Themes[0];
        _selectedLanguage = Languages[0];

        SelectSectionCommand = commands.Create<SettingsSection>(section =>
        {
            SelectedSection = section;
            SectionNavigationRequested?.Invoke(section);
        }, _ => !IsSaving);
        CloseCommand = commands.Create(() => CloseRequested?.Invoke(this, EventArgs.Empty), () => !IsSaving);
        SaveCommand = commands.CreateAsync(SaveAsync, () => CanSave, activationOwner: activationOwner);
        RestoreDefaultsCommand = commands.CreateAsync(RestoreDefaultsAsync, () => !IsSaving, activationOwner: activationOwner);
        ChooseOutputDirectoryCommand = commands.CreateAsync(ChooseOutputDirectoryAsync, () => IsReady && !IsSaving, activationOwner: activationOwner);
        OpenSettingsDirectoryCommand = commands.CreateAsync(OpenSettingsDirectoryAsync, () => !IsSaving, activationOwner: activationOwner);
    }

    public IReadOnlyList<DesktopChoiceOption<CompressionMode>> CompressionModes { get; }
    public IReadOnlyList<DesktopChoiceOption<OutputImageFormat>> ConversionFormats { get; }
    public IReadOnlyList<DesktopChoiceOption<OutputLocationMode>> OutputLocations { get; }
    public IReadOnlyList<DesktopChoiceOption<OverwritePolicy>> OverwritePolicies { get; }
    public IReadOnlyList<DesktopChoiceOption<ThemeMode>> Themes { get; }
    public IReadOnlyList<DesktopChoiceOption<string?>> Languages { get; }

    public SettingsSection SelectedSection
    {
        get => _selectedSection;
        set
        {
            if (SetProperty(ref _selectedSection, value))
            {
                OnPropertyChanged(nameof(IsCompressionSection));
                OnPropertyChanged(nameof(IsConversionSection));
                OnPropertyChanged(nameof(IsOutputSection));
                OnPropertyChanged(nameof(IsAboutSection));
                OnPropertyChanged(nameof(IsDefaultsSection));
                OnPropertyChanged(nameof(SectionTitle));
            }
        }
    }

    public DesktopChoiceOption<CompressionMode> SelectedCompressionMode
    {
        get => _selectedCompressionMode;
        set { if (value is not null && SetProperty(ref _selectedCompressionMode, value)) DraftChanged(); }
    }

    public decimal CustomCompressionQuality
    {
        get => _customCompressionQuality;
        set { if (SetProperty(ref _customCompressionQuality, value)) { OnPropertyChanged(nameof(CustomCompressionQualitySlider)); DraftChanged(); } }
    }

    public double CustomCompressionQualitySlider
    {
        get => decimal.ToDouble(CustomCompressionQuality);
        set => CustomCompressionQuality = decimal.Round(
            Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture),
            0,
            MidpointRounding.AwayFromZero);
    }

    public DesktopChoiceOption<OutputImageFormat> SelectedConversionFormat
    {
        get => _selectedConversionFormat;
        set { if (value is not null && SetProperty(ref _selectedConversionFormat, value)) DraftChanged(); }
    }

    public decimal ConversionQuality
    {
        get => _conversionQuality;
        set { if (SetProperty(ref _conversionQuality, value)) { OnPropertyChanged(nameof(ConversionQualitySlider)); DraftChanged(); } }
    }

    public double ConversionQualitySlider
    {
        get => decimal.ToDouble(ConversionQuality);
        set => ConversionQuality = decimal.Round(
            Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture),
            0,
            MidpointRounding.AwayFromZero);
    }

    public string BackgroundHex
    {
        get => _backgroundHex;
        set { if (SetProperty(ref _backgroundHex, value ?? string.Empty)) DraftChanged(); }
    }

    public bool RemoveMetadata
    {
        get => _removeMetadata;
        set { if (SetProperty(ref _removeMetadata, value)) DraftChanged(); }
    }

    public DesktopChoiceOption<OutputLocationMode> SelectedOutputLocation
    {
        get => _selectedOutputLocation;
        set { if (value is not null && SetProperty(ref _selectedOutputLocation, value)) DraftChanged(); }
    }

    public string SubfolderName
    {
        get => _subfolderName;
        set
        {
            if (SetProperty(ref _subfolderName, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(SubfolderDestinationHint));
                DraftChanged();
            }
        }
    }

    public string CustomOutputDirectory
    {
        get => _customOutputDirectory;
        set { if (SetProperty(ref _customOutputDirectory, value ?? string.Empty)) DraftChanged(); }
    }

    public string FileNamePattern
    {
        get => _fileNamePattern;
        set { if (SetProperty(ref _fileNamePattern, value ?? string.Empty)) DraftChanged(); }
    }

    public DesktopChoiceOption<OverwritePolicy> SelectedOverwritePolicy
    {
        get => _selectedOverwritePolicy;
        set { if (value is not null && SetProperty(ref _selectedOverwritePolicy, value)) DraftChanged(); }
    }

    public DesktopChoiceOption<ThemeMode> SelectedTheme
    {
        get => _selectedTheme;
        set { if (value is not null && SetProperty(ref _selectedTheme, value)) DraftChanged(); }
    }

    public DesktopChoiceOption<string?> SelectedLanguage
    {
        get => _selectedLanguage;
        set { if (value is not null && SetProperty(ref _selectedLanguage, value)) DraftChanged(); }
    }

    public bool IsLoaded { get => _isLoaded; private set { if (SetProperty(ref _isLoaded, value)) NotifyState(); } }
    public bool IsLoading { get => _isLoading; private set { if (SetProperty(ref _isLoading, value)) NotifyState(); } }
    public bool IsSaving { get => _isSaving; private set { if (SetProperty(ref _isSaving, value)) NotifyState(); } }
    public bool IsDirty { get => _isDirty; private set { if (SetProperty(ref _isDirty, value)) NotifyState(); } }
    public bool IsReady => IsLoaded && !IsLoading;
    public bool IsLoadFailure => !IsLoading && !IsLoaded && HasError;
    public bool IsCompressionSection => SelectedSection == SettingsSection.Compression;
    public bool IsConversionSection => SelectedSection == SettingsSection.Conversion;
    public bool IsOutputSection => SelectedSection == SettingsSection.Output;
    public bool IsAboutSection => SelectedSection == SettingsSection.About;
    public bool IsDefaultsSection => SelectedSection != SettingsSection.About;
    public bool IsCustomCompression => SelectedCompressionMode.Value == CompressionMode.Custom;
    public bool ConversionUsesQuality => SelectedConversionFormat.Value is OutputImageFormat.Jpeg or OutputImageFormat.WebP;
    public bool IsSubfolderOutput => SelectedOutputLocation.Value == OutputLocationMode.Subfolder;
    public bool IsSameAsInputOutput => SelectedOutputLocation.Value == OutputLocationMode.SameAsInput;
    public bool IsCustomDirectoryOutput => SelectedOutputLocation.Value == OutputLocationMode.CustomDirectory;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasSaveMessage => !string.IsNullOrWhiteSpace(SaveMessage);
    public bool IsDraftValid => TryBuildSettings(out _, out _);
    public bool HasDraftError => !string.IsNullOrWhiteSpace(DraftError);
    // Keep the explicit submit action available while a dirty draft is ready.
    // Validation belongs to the submit boundary so the user receives a concrete
    // window-level message instead of a button that silently remains disabled.
    public bool CanSave => IsReady && IsDirty && !IsSaving;
    public string? DraftError { get { TryBuildSettings(out _, out var error); return error; } }
    public string? ErrorMessage { get => _errorMessage; private set { if (SetProperty(ref _errorMessage, value)) NotifyState(); } }
    public string? SaveMessage { get => _saveMessage; private set { if (SetProperty(ref _saveMessage, value)) OnPropertyChanged(nameof(HasSaveMessage)); } }
    public bool IsSaveMessageSuccess { get => _isSaveMessageSuccess; private set => SetProperty(ref _isSaveMessageSuccess, value); }
    public string SectionTitle => SelectedSection switch
    {
        SettingsSection.Compression => _text.GetString("Settings.Section.Compression"),
        SettingsSection.Conversion => _text.GetString("Settings.Section.Conversion"),
        SettingsSection.Output => _text.GetString("Settings.Section.Output"),
        SettingsSection.About => _text.GetString("Settings.Section.About"),
        _ => string.Empty
    };
    public string SameFormatSummary => _text.Format("Settings.Dynamic.SameFormatSummary", _sameFormatQuality.Value);
    public string SubfolderDestinationHint =>
        _text.Format("Common.Output.SubfolderDestinationHint", string.IsNullOrWhiteSpace(SubfolderName) ? "AtomPix_Output" : SubfolderName.Trim());
    public string VersionText => _text.Format(
        "Settings.Dynamic.Version",
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? _text.GetString("Settings.Dynamic.DevelopmentVersion"));
    public string RuntimeText => $".NET {Environment.Version.ToString(3)} · Avalonia {typeof(Avalonia.Application).Assembly.GetName().Version?.ToString(3)} · AtomUI {typeof(AtomUI.Desktop.Controls.Button).Assembly.GetName().Version?.ToString(3)}";
    public string PlatformText => $"{RuntimeInformation.OSDescription} · {RuntimeInformation.ProcessArchitecture}";
    public string ImageEngineText => _text.GetString("Settings.Dynamic.ImageEngine");

    public IRelayCommand<SettingsSection> SelectSectionCommand { get; }
    public IRelayCommand CloseCommand { get; }
    public IAsyncRelayCommand SaveCommand { get; }
    public IAsyncRelayCommand RestoreDefaultsCommand { get; }
    public IAsyncRelayCommand ChooseOutputDirectoryCommand { get; }
    public IAsyncRelayCommand OpenSettingsDirectoryCommand { get; }
    public DiagnosticErrorViewModel Diagnostic { get; }

    public event Action<SettingsSection>? SectionNavigationRequested;
    public event EventHandler? CloseRequested;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (IsLoaded) return;

        await _loadGate.WaitAsync(cancellationToken);
        try
        {
            // Startup preloading and an early settings click can overlap. The second
            // caller must await the in-flight load instead of returning while the
            // dialog is still showing its lightweight loading state.
            if (IsLoaded) return;

            var loadCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _loadCancellation = loadCancellation;
            IsLoading = true;
            ErrorMessage = null;
            Diagnostic.Clear();
            IsSaveMessageSuccess = false;
            SaveMessage = null;

            var result = await _loadSettings.ExecuteAsync(new LoadSettingsRequest(), loadCancellation.Token);
            if (loadCancellation.IsCancellationRequested) return;

            if (!result.Succeeded)
            {
                ErrorMessage = DesktopErrorText.FromWorkflow(result.Error, _text);
                Diagnostic.Set(result.Error);
                return;
            }

            _originalSettings = result.Value!.Settings;
            ApplySettings(_originalSettings, dirty: false);
            IsLoaded = true;
        }
        finally
        {
            IsLoading = false;
            var loadCancellation = Interlocked.Exchange(ref _loadCancellation, null);
            loadCancellation?.Dispose();
            _loadGate.Release();
        }
    }

    public Task<bool> TryLeaveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(!IsDirty || DiscardAndLeave());
    }

    protected override void OnDisposed()
    {
        Interlocked.Exchange(ref _settingsSubscription, null)?.Dispose();
        var cancellation = Interlocked.Exchange(ref _loadCancellation, null);
        if (cancellation is null) return;
        cancellation.Cancel();
        cancellation.Dispose();
    }

    protected override ValueTask OnActivatedAsync(
        ActivationContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_applicationState is not null)
        {
            _settingsSubscription = _applicationState.AppSettings.Observe(args =>
            {
                void Apply() => ApplyApplicationState(args.NewValue);
                if (_dispatcher is null) Apply();
                else _dispatcher.Post(Apply);
            });
        }

        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnDeactivatedAsync(CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _settingsSubscription, null)?.Dispose();
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    private async Task<bool> SaveAsync(CancellationToken cancellationToken)
    {
        if (!TryBuildSettings(out var draft, out var validationError))
        {
            ErrorMessage = validationError;
            return false;
        }

        IsSaving = true;
        ErrorMessage = null;
        Diagnostic.Clear();
        IsSaveMessageSuccess = false;
        SaveMessage = null;
        try
        {
            var result = await _saveSettings.ExecuteAsync(new SaveSettingsRequest(draft!), cancellationToken);
            if (!result.Succeeded)
            {
                ErrorMessage = DesktopErrorText.FromWorkflow(result.Error, _text);
                Diagnostic.Set(result.Error);
                return false;
            }

            _originalSettings = draft;
            IsDirty = false;
            IsSaveMessageSuccess = true;
            SaveMessage = _text.Format("Settings.Message.Saved", DateTime.Now);
            return true;
        }
        finally
        {
            IsSaving = false;
        }
    }

    private void ApplyApplicationState(AppSettingsState state)
    {
        switch (state)
        {
            case AppSettingsState.Ready ready when !IsDirty || IsSaving:
                _originalSettings = ready.Value;
                ApplySettings(ready.Value, dirty: false);
                IsLoaded = true;
                ErrorMessage = null;
                Diagnostic.Clear();
                break;
            case AppSettingsState.Faulted faulted when !IsLoaded:
                ErrorMessage = DesktopErrorText.FromWorkflow(faulted.Error, _text);
                Diagnostic.Set(faulted.Error);
                break;
        }
    }

    private async Task RestoreDefaultsAsync(CancellationToken cancellationToken)
    {
        var confirmed = await _dialogs.ConfirmAsync(
            _text.GetString("Settings.Dialog.RestoreDefaults.Title"),
            _text.GetString("Settings.Dialog.RestoreDefaults.Message"),
            _text.GetString("Settings.Action.RestoreDefaults"),
            _text.GetString("Common.Action.Cancel"),
            cancellationToken);
        if (!confirmed) return;

        ApplySettings(AppSettings.Default, dirty: true);
        IsLoaded = true;
        ErrorMessage = null;
        Diagnostic.Clear();
        IsSaveMessageSuccess = false;
        SaveMessage = _text.GetString("Settings.Message.DefaultDraftRestored");
    }

    private async Task ChooseOutputDirectoryAsync(CancellationToken cancellationToken)
    {
        var selected = await _picker.PickFolderAsync(cancellationToken);
        if (selected.Status == DesktopSelectionStatus.Selected && selected.Paths.Count > 0)
        {
            CustomOutputDirectory = selected.Paths[0];
        }
        else if (selected.Status is DesktopSelectionStatus.Unavailable or DesktopSelectionStatus.Failed)
        {
            ErrorMessage = DesktopErrorText.FromPicker(selected.ErrorMessage, _text);
            Diagnostic.Clear();
        }
    }

    private async Task OpenSettingsDirectoryAsync(CancellationToken cancellationToken)
    {
        if (!await _launcher.OpenDirectoryAsync(_paths.AppDataDirectory.Value, cancellationToken))
        {
            ErrorMessage = _text.GetString("Settings.Error.OpenDirectoryFailed");
            Diagnostic.Clear();
        }
    }

    private bool DiscardAndLeave()
    {
        if (_originalSettings is not null) ApplySettings(_originalSettings, dirty: false);
        else IsDirty = false;
        ErrorMessage = null;
        Diagnostic.Clear();
        IsSaveMessageSuccess = false;
        SaveMessage = null;
        return true;
    }

    private void ApplySettings(AppSettings settings, bool dirty)
    {
        _isApplying = true;
        try
        {
            SelectedCompressionMode = CompressionModes.First(option => option.Value == settings.DefaultCompressionProfile.Mode);
            if (settings.DefaultCompressionProfile.Mode == CompressionMode.Custom)
                CustomCompressionQuality = settings.DefaultCompressionProfile.Quality!.Value.Value;
            SelectedConversionFormat = ConversionFormats.First(option => option.Value == settings.DefaultConversionProfile.OutputFormat);
            ConversionQuality = settings.DefaultConversionProfile.Quality?.Value ?? 80;
            BackgroundHex = settings.DefaultConversionProfile.TransparencyPolicy.OpaqueBackgroundColor.ToHexString();
            RemoveMetadata = settings.DefaultCompressionProfile.MetadataPolicy == MetadataPolicy.Remove;
            _sameFormatQuality = settings.DefaultSameFormatEncodingPolicy.LossyQuality;
            SelectedOutputLocation = OutputLocations.First(option => option.Value == settings.DefaultOutputPolicy.LocationPolicy.Mode);
            SubfolderName = settings.DefaultOutputPolicy.LocationPolicy.SubfolderName ?? "AtomPix_Output";
            CustomOutputDirectory = settings.DefaultOutputPolicy.LocationPolicy.CustomDirectory ?? string.Empty;
            FileNamePattern = settings.DefaultOutputPolicy.NamingPolicy.GetBasePattern();
            SelectedOverwritePolicy = OverwritePolicies.First(option => option.Value == settings.DefaultOutputPolicy.OverwritePolicy);
            SelectedTheme = Themes.First(option => option.Value == settings.ThemeMode);
            SelectedLanguage = Languages.FirstOrDefault(option => option.Value == settings.Language) ?? Languages[0];
        }
        finally
        {
            _isApplying = false;
        }

        IsDirty = dirty;
        NotifyDraft();
    }

    private bool TryBuildSettings(out AppSettings? settings, out string? error)
    {
        settings = null;
        error = null;
        try
        {
            if (SelectedCompressionMode.Value == CompressionMode.Custom
                && (CustomCompressionQuality is < 1 or > 100 || CustomCompressionQuality != decimal.Truncate(CustomCompressionQuality)))
                throw new ArgumentOutOfRangeException(nameof(CustomCompressionQuality));
            if (ConversionUsesQuality
                && (ConversionQuality is < 1 or > 100 || ConversionQuality != decimal.Truncate(ConversionQuality)))
                throw new ArgumentOutOfRangeException(nameof(ConversionQuality));

            var metadata = RemoveMetadata ? MetadataPolicy.Remove : MetadataPolicy.Preserve;
            var compression = SelectedCompressionMode.Value switch
            {
                CompressionMode.Smart => new CompressionProfile(CompressionMode.Smart, null, metadata),
                CompressionMode.HighQuality => new CompressionProfile(CompressionMode.HighQuality, new ImageQuality(90), metadata),
                CompressionMode.Balanced => new CompressionProfile(CompressionMode.Balanced, new ImageQuality(80), metadata),
                CompressionMode.Maximum => new CompressionProfile(CompressionMode.Maximum, new ImageQuality(65), metadata),
                CompressionMode.Custom => new CompressionProfile(CompressionMode.Custom, new ImageQuality(decimal.ToInt32(CustomCompressionQuality)), metadata),
                _ => throw new InvalidOperationException(_text.GetString("Settings.Diagnostic.UnsupportedCompressionMode"))
            };
            var conversionQuality = ConversionUsesQuality
                ? new ImageQuality(decimal.ToInt32(ConversionQuality))
                : (ImageQuality?)null;
            var conversion = new ConversionProfile(
                SelectedConversionFormat.Value,
                conversionQuality,
                metadata,
                new TransparencyPolicy(RgbColor.Parse(BackgroundHex)));
            var sameFormat = new SameFormatEncodingPolicy(_sameFormatQuality, metadata);
            var location = SelectedOutputLocation.Value switch
            {
                OutputLocationMode.SameAsInput => new OutputLocationPolicy(OutputLocationMode.SameAsInput, null, null),
                OutputLocationMode.Subfolder => new OutputLocationPolicy(OutputLocationMode.Subfolder, null, SubfolderName.Trim()),
                OutputLocationMode.CustomDirectory => new OutputLocationPolicy(OutputLocationMode.CustomDirectory, CustomOutputDirectory.Trim(), null),
                _ => throw new InvalidOperationException(_text.GetString("Settings.Diagnostic.UnsupportedOutputLocation"))
            };
            var naming = new OutputNamingPolicy(OutputNamingMode.CustomPattern, null, FileNamePattern.Trim());
            var output = new OutputPolicy(location, naming, SelectedOverwritePolicy.Value);
            settings = new AppSettings(
                compression,
                conversion,
                sameFormat,
                output,
                SelectedTheme.Value,
                SelectedLanguage.Value,
                _originalSettings?.RecentItems ?? AppSettings.Default.RecentItems);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException or InvalidOperationException)
        {
            error = exception switch
            {
                FormatException => _text.GetString("Settings.Validation.BackgroundColor"),
                _ when string.IsNullOrWhiteSpace(FileNamePattern) => _text.GetString("Settings.Validation.FileNamePatternRequired"),
                _ when SelectedCompressionMode.Value == CompressionMode.Custom && (CustomCompressionQuality is < 1 or > 100 || CustomCompressionQuality != decimal.Truncate(CustomCompressionQuality)) => _text.GetString("Settings.Validation.CompressionQuality"),
                _ when ConversionUsesQuality && (ConversionQuality is < 1 or > 100 || ConversionQuality != decimal.Truncate(ConversionQuality)) => _text.GetString("Settings.Validation.ConversionQuality"),
                _ => _text.GetString("Settings.Validation.InvalidDraft")
            };
            return false;
        }
    }

    private void DraftChanged()
    {
        if (_isApplying) return;
        IsDirty = true;
        IsSaveMessageSuccess = false;
        SaveMessage = null;
        NotifyDraft();
    }

    private void NotifyDraft()
    {
        OnPropertyChanged(nameof(IsCustomCompression));
        OnPropertyChanged(nameof(ConversionUsesQuality));
        OnPropertyChanged(nameof(IsSubfolderOutput));
        OnPropertyChanged(nameof(IsSameAsInputOutput));
        OnPropertyChanged(nameof(IsCustomDirectoryOutput));
        OnPropertyChanged(nameof(IsDraftValid));
        OnPropertyChanged(nameof(DraftError));
        OnPropertyChanged(nameof(HasDraftError));
        OnPropertyChanged(nameof(SameFormatSummary));
        SaveCommand.NotifyCanExecuteChanged();
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(IsReady));
        OnPropertyChanged(nameof(IsLoadFailure));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(HasError));
        SaveCommand.NotifyCanExecuteChanged();
        RestoreDefaultsCommand.NotifyCanExecuteChanged();
        ChooseOutputDirectoryCommand.NotifyCanExecuteChanged();
        OpenSettingsDirectoryCommand.NotifyCanExecuteChanged();
        SelectSectionCommand.NotifyCanExecuteChanged();
        CloseCommand.NotifyCanExecuteChanged();
    }
}
