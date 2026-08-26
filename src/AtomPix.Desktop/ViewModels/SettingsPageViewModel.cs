namespace AtomPix.Desktop.ViewModels;

using System.Runtime.InteropServices;
using System.Reflection;
using AtomPix.Core.Compression;
using AtomPix.Core.Conversion;
using AtomPix.Core.Output;
using AtomPix.Core.Ports;
using AtomPix.Core.Resize;
using AtomPix.Core.Settings;
using AtomPix.Desktop.Platform;
using AtomPix.Workflows.Settings;

public enum SettingsSection
{
    Compression,
    Conversion,
    Output,
    About
}

public sealed class SettingsPageViewModel : ObservableObject, IDisposable
{
    private readonly LoadSettingsWorkflow _loadSettings;
    private readonly SaveSettingsWorkflow _saveSettings;
    private readonly IDesktopDialogService _dialogs;
    private readonly IDesktopPickerService _picker;
    private readonly IDesktopLauncherService _launcher;
    private readonly IAppPathProvider _paths;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private CancellationTokenSource? _loadCancellation;
    private AppSettings? _originalSettings;
    private bool _isApplying;
    private bool _isLoaded;
    private bool _isLoading;
    private bool _isSaving;
    private bool _isDirty;
    private string? _errorMessage;
    private string? _saveMessage;
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
    private bool _recentEnabled = true;
    private decimal _recentMaxCount = 20;

    public SettingsPageViewModel(
        LoadSettingsWorkflow loadSettings,
        SaveSettingsWorkflow saveSettings,
        IDesktopDialogService dialogs,
        IDesktopPickerService picker,
        IDesktopLauncherService launcher,
        IAppPathProvider paths,
        IDesktopClipboardService clipboard)
    {
        _loadSettings = loadSettings ?? throw new ArgumentNullException(nameof(loadSettings));
        _saveSettings = saveSettings ?? throw new ArgumentNullException(nameof(saveSettings));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _picker = picker ?? throw new ArgumentNullException(nameof(picker));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        Diagnostic = new DiagnosticErrorViewModel(clipboard);

        CompressionModes =
        [
            new("智能", CompressionMode.Smart),
            new("高质量", CompressionMode.HighQuality),
            new("平衡", CompressionMode.Balanced),
            new("极限", CompressionMode.Maximum),
            new("自定义", CompressionMode.Custom)
        ];
        ConversionFormats =
        [
            new("JPEG", OutputImageFormat.Jpeg),
            new("PNG", OutputImageFormat.Png),
            new("WebP", OutputImageFormat.WebP)
        ];
        OutputLocations =
        [
            new("原图旁的输出子目录", OutputLocationMode.Subfolder),
            new("与原图相同目录", OutputLocationMode.SameAsInput),
            new("自定义目录", OutputLocationMode.CustomDirectory)
        ];
        OverwritePolicies =
        [
            new("自动重命名", OverwritePolicy.AutoRename),
            new("跳过已有文件", OverwritePolicy.Skip),
            new("覆盖已有输出", OverwritePolicy.Overwrite)
        ];
        Themes =
        [
            new("跟随系统", ThemeMode.System),
            new("浅色", ThemeMode.Light),
            new("深色", ThemeMode.Dark)
        ];
        Languages =
        [
            new("跟随系统", null),
            new("简体中文", "zh-CN")
        ];

        _selectedCompressionMode = CompressionModes[0];
        _selectedConversionFormat = ConversionFormats[2];
        _selectedOutputLocation = OutputLocations[0];
        _selectedOverwritePolicy = OverwritePolicies[0];
        _selectedTheme = Themes[0];
        _selectedLanguage = Languages[0];

        SelectSectionCommand = new RelayCommand<SettingsSection>(section =>
        {
            SelectedSection = section;
            SectionNavigationRequested?.Invoke(section);
        }, _ => !IsSaving);
        CloseCommand = new RelayCommand<object?>(_ => CloseRequested?.Invoke(this, EventArgs.Empty), _ => !IsSaving);
        SaveCommand = new AsyncCommand(SaveAsync, () => CanSave);
        RestoreDefaultsCommand = new AsyncCommand(RestoreDefaultsAsync, () => !IsSaving);
        ChooseOutputDirectoryCommand = new AsyncCommand(ChooseOutputDirectoryAsync, () => IsReady && !IsSaving);
        OpenSettingsDirectoryCommand = new AsyncCommand(OpenSettingsDirectoryAsync, () => !IsSaving);
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

    public bool RecentEnabled
    {
        get => _recentEnabled;
        set { if (SetProperty(ref _recentEnabled, value)) DraftChanged(); }
    }

    public decimal RecentMaxCount
    {
        get => _recentMaxCount;
        set { if (SetProperty(ref _recentMaxCount, value)) DraftChanged(); }
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
    public string SectionTitle => SelectedSection switch
    {
        SettingsSection.Compression => "压缩配置",
        SettingsSection.Conversion => "转换配置",
        SettingsSection.Output => "输出配置",
        SettingsSection.About => "关于 AtomPix",
        _ => string.Empty
    };
    public string SameFormatSummary => $"Resize/Crop：有损质量 {_sameFormatQuality.Value} · 元数据跟随公共开关 · ICC 保留";
    public string SubfolderDestinationHint =>
        $"图片将保存到：每张原图所在目录 / {(string.IsNullOrWhiteSpace(SubfolderName) ? "AtomPix_Output" : SubfolderName.Trim())}";
    public string VersionText => $"AtomPix {Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "开发版本"}";
    public string RuntimeText => $".NET {Environment.Version.ToString(3)} · Avalonia {typeof(Avalonia.Application).Assembly.GetName().Version?.ToString(3)} · AtomUI {typeof(AtomUI.Desktop.Controls.Button).Assembly.GetName().Version?.ToString(3)}";
    public string PlatformText => $"{RuntimeInformation.OSDescription} · {RuntimeInformation.ProcessArchitecture}";
    public string ImageEngineText => "图像引擎：ImageMagick（Magick.NET）";

    public RelayCommand<SettingsSection> SelectSectionCommand { get; }
    public RelayCommand<object?> CloseCommand { get; }
    public AsyncCommand SaveCommand { get; }
    public AsyncCommand RestoreDefaultsCommand { get; }
    public AsyncCommand ChooseOutputDirectoryCommand { get; }
    public AsyncCommand OpenSettingsDirectoryCommand { get; }
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
            SaveMessage = null;

            var result = await _loadSettings.ExecuteAsync(new LoadSettingsRequest(), loadCancellation.Token);
            if (loadCancellation.IsCancellationRequested) return;

            if (!result.Succeeded)
            {
                ErrorMessage = DesktopErrorText.FromWorkflow(result.Error);
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

    public void Dispose()
    {
        var cancellation = Interlocked.Exchange(ref _loadCancellation, null);
        if (cancellation is null) return;
        cancellation.Cancel();
        cancellation.Dispose();
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
        SaveMessage = null;
        try
        {
            var result = await _saveSettings.ExecuteAsync(new SaveSettingsRequest(draft!), cancellationToken);
            if (!result.Succeeded)
            {
                ErrorMessage = DesktopErrorText.FromWorkflow(result.Error);
                Diagnostic.Set(result.Error);
                return false;
            }

            _originalSettings = draft;
            IsDirty = false;
            SaveMessage = $"设置已保存 · {DateTime.Now:HH:mm}";
            return true;
        }
        finally
        {
            IsSaving = false;
        }
    }

    private async Task RestoreDefaultsAsync(CancellationToken cancellationToken)
    {
        var confirmed = await _dialogs.ConfirmAsync(
            "恢复默认设置？",
            "默认值只会替换当前草稿；点击“保存设置”后才会写入磁盘。",
            "恢复默认",
            "取消",
            cancellationToken);
        if (!confirmed) return;

        ApplySettings(AppSettings.Default, dirty: true);
        IsLoaded = true;
        ErrorMessage = null;
        Diagnostic.Clear();
        SaveMessage = "已恢复为默认草稿，尚未保存。";
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
            ErrorMessage = DesktopErrorText.FromPicker(selected.ErrorMessage);
            Diagnostic.Clear();
        }
    }

    private async Task OpenSettingsDirectoryAsync(CancellationToken cancellationToken)
    {
        if (!await _launcher.OpenDirectoryAsync(_paths.AppDataDirectory.Value, cancellationToken))
        {
            ErrorMessage = "无法打开设置目录。";
            Diagnostic.Clear();
        }
    }

    private bool DiscardAndLeave()
    {
        if (_originalSettings is not null) ApplySettings(_originalSettings, dirty: false);
        else IsDirty = false;
        ErrorMessage = null;
        Diagnostic.Clear();
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
            RecentEnabled = settings.RecentItems.Enabled;
            RecentMaxCount = settings.RecentItems.MaxCount;
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
            if (RecentMaxCount < 1 || RecentMaxCount != decimal.Truncate(RecentMaxCount))
                throw new ArgumentOutOfRangeException(nameof(RecentMaxCount));

            var metadata = RemoveMetadata ? MetadataPolicy.Remove : MetadataPolicy.Preserve;
            var compression = SelectedCompressionMode.Value switch
            {
                CompressionMode.Smart => new CompressionProfile(CompressionMode.Smart, null, metadata),
                CompressionMode.HighQuality => new CompressionProfile(CompressionMode.HighQuality, new ImageQuality(90), metadata),
                CompressionMode.Balanced => new CompressionProfile(CompressionMode.Balanced, new ImageQuality(80), metadata),
                CompressionMode.Maximum => new CompressionProfile(CompressionMode.Maximum, new ImageQuality(65), metadata),
                CompressionMode.Custom => new CompressionProfile(CompressionMode.Custom, new ImageQuality(decimal.ToInt32(CustomCompressionQuality)), metadata),
                _ => throw new InvalidOperationException("不支持的压缩模式。")
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
                _ => throw new InvalidOperationException("不支持的输出位置。")
            };
            var naming = new OutputNamingPolicy(OutputNamingMode.CustomPattern, null, FileNamePattern.Trim());
            var output = new OutputPolicy(location, naming, SelectedOverwritePolicy.Value);
            var recent = new RecentItemsSettings(RecentEnabled, decimal.ToInt32(RecentMaxCount));
            settings = new AppSettings(
                compression,
                conversion,
                sameFormat,
                output,
                SelectedTheme.Value,
                SelectedLanguage.Value,
                recent);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException or InvalidOperationException)
        {
            error = exception switch
            {
                FormatException => "透明背景色必须使用 #RRGGBB 格式。",
                _ when string.IsNullOrWhiteSpace(FileNamePattern) => "默认文件名格式不能为空。",
                _ when SelectedCompressionMode.Value == CompressionMode.Custom && (CustomCompressionQuality is < 1 or > 100 || CustomCompressionQuality != decimal.Truncate(CustomCompressionQuality)) => "自定义压缩质量必须是 1 到 100 的整数。",
                _ when ConversionUsesQuality && (ConversionQuality is < 1 or > 100 || ConversionQuality != decimal.Truncate(ConversionQuality)) => "转换质量必须是 1 到 100 的整数。",
                _ when RecentMaxCount < 1 || RecentMaxCount != decimal.Truncate(RecentMaxCount) => "最近记录最大条数必须是正整数。",
                _ => "设置草稿包含无效值，请检查输出位置和文件名格式。"
            };
            return false;
        }
    }

    private void DraftChanged()
    {
        if (_isApplying) return;
        IsDirty = true;
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
