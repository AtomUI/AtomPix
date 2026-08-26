namespace AtomPix.Desktop.ViewModels;

using AtomPix.Core.Output;
using AtomPix.Desktop.Platform;

public sealed class OutputPolicyEditorViewModel : ObservableObject
{
    private readonly IDesktopPickerService _picker;
    private readonly Action _draftChanged;
    private readonly Action<string>? _feedbackRequested;
    private DesktopChoiceOption<OutputLocationMode> _selectedLocation;
    private DesktopChoiceOption<OutputNamingMode> _selectedNaming;
    private DesktopChoiceOption<OverwritePolicy> _selectedOverwrite;
    private string _subfolderName = "AtomPix_Output";
    private string _customDirectory = string.Empty;
    private string _fileNameSuffix = "_atompix";
    private string _customFileNamePattern = "{name}_atompix";
    private string? _pickerError;

    public OutputPolicyEditorViewModel(
        IDesktopPickerService picker,
        Action? draftChanged = null,
        Action<string>? feedbackRequested = null)
    {
        _picker = picker ?? throw new ArgumentNullException(nameof(picker));
        _draftChanged = draftChanged ?? (() => { });
        _feedbackRequested = feedbackRequested;
        Locations =
        [
            new("原图旁子目录", OutputLocationMode.Subfolder),
            new("原图目录", OutputLocationMode.SameAsInput),
            new("自定义目录", OutputLocationMode.CustomDirectory)
        ];
        OverwritePolicies =
        [
            new("自动重命名", OverwritePolicy.AutoRename),
            new("跳过已有文件", OverwritePolicy.Skip),
            new("覆盖已有输出", OverwritePolicy.Overwrite)
        ];
        NamingModes =
        [
            new("保留原文件名", OutputNamingMode.KeepOriginalName),
            new("添加后缀", OutputNamingMode.AppendSuffix),
            new("自定义格式", OutputNamingMode.CustomPattern)
        ];
        _selectedLocation = Locations[0];
        _selectedNaming = NamingModes[1];
        _selectedOverwrite = OverwritePolicies[0];
        ChooseDirectoryCommand = new AsyncCommand(ChooseDirectoryAsync);
        InsertNameTokenCommand = new RelayCommand<object?>(_ => CustomFileNamePattern += "{name}");
        InsertIndexTokenCommand = new RelayCommand<object?>(
            _ => CustomFileNamePattern += "{index}",
            _ => !CustomFileNamePattern.Contains("{index}", StringComparison.Ordinal));
    }

    public IReadOnlyList<DesktopChoiceOption<OutputLocationMode>> Locations { get; }
    public IReadOnlyList<DesktopChoiceOption<OutputNamingMode>> NamingModes { get; }
    public IReadOnlyList<DesktopChoiceOption<OverwritePolicy>> OverwritePolicies { get; }

    public DesktopChoiceOption<OutputLocationMode> SelectedLocation
    {
        get => _selectedLocation;
        set
        {
            if (value is not null && SetProperty(ref _selectedLocation, value))
            {
                NotifyDraftChanged();
            }
        }
    }

    public DesktopChoiceOption<OutputNamingMode> SelectedNaming
    {
        get => _selectedNaming;
        set
        {
            if (value is not null && SetProperty(ref _selectedNaming, value))
            {
                NotifyDraftChanged();
            }
        }
    }

    public DesktopChoiceOption<OverwritePolicy> SelectedOverwrite
    {
        get => _selectedOverwrite;
        set
        {
            if (value is not null && SetProperty(ref _selectedOverwrite, value))
            {
                NotifyDraftChanged();
            }
        }
    }

    public string SubfolderName
    {
        get => _subfolderName;
        set
        {
            if (SetProperty(ref _subfolderName, value ?? string.Empty))
            {
                NotifyDraftChanged();
            }
        }
    }

    public string CustomDirectory
    {
        get => _customDirectory;
        set
        {
            if (SetProperty(ref _customDirectory, value ?? string.Empty))
            {
                NotifyDraftChanged();
            }
        }
    }

    public string FileNameSuffix
    {
        get => _fileNameSuffix;
        set
        {
            if (SetProperty(ref _fileNameSuffix, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(FileNamePattern));
                OnPropertyChanged(nameof(NamingPreview));
                NotifyDraftChanged();
            }
        }
    }

    public string CustomFileNamePattern
    {
        get => _customFileNamePattern;
        set
        {
            if (SetProperty(ref _customFileNamePattern, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(FileNamePattern));
                OnPropertyChanged(nameof(NamingPreview));
                InsertIndexTokenCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(CanInsertIndexToken));
                NotifyDraftChanged();
            }
        }
    }

    // Compatibility surface used by batch orchestration and existing callers. Assigning a
    // pattern intentionally switches the editor to the fully custom naming mode.
    public string FileNamePattern
    {
        get => SelectedNaming.Value switch
        {
            OutputNamingMode.KeepOriginalName => "{name}",
            OutputNamingMode.AppendSuffix => "{name}" + FileNameSuffix,
            OutputNamingMode.CustomPattern => CustomFileNamePattern,
            _ => CustomFileNamePattern
        };
        set
        {
            _customFileNamePattern = value ?? string.Empty;
            _selectedNaming = NamingModes.First(option => option.Value == OutputNamingMode.CustomPattern);
            OnPropertyChanged(nameof(SelectedNaming));
            OnPropertyChanged(nameof(CustomFileNamePattern));
            OnPropertyChanged(nameof(FileNamePattern));
            InsertIndexTokenCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CanInsertIndexToken));
            NotifyDraftChanged();
        }
    }

    public bool CanInsertIndexToken => !CustomFileNamePattern.Contains("{index}", StringComparison.Ordinal);

    public bool IsSubfolder => SelectedLocation.Value == OutputLocationMode.Subfolder;
    public bool IsSameAsInput => SelectedLocation.Value == OutputLocationMode.SameAsInput;
    public bool IsCustomDirectory => SelectedLocation.Value == OutputLocationMode.CustomDirectory;
    public bool IsKeepOriginalName => SelectedNaming.Value == OutputNamingMode.KeepOriginalName;
    public bool IsAppendSuffix => SelectedNaming.Value == OutputNamingMode.AppendSuffix;
    public bool IsCustomPattern => SelectedNaming.Value == OutputNamingMode.CustomPattern;
    public string NamingPreview => ExpandNamingPreview(FileNamePattern);
    public string SubfolderDestinationHint => string.IsNullOrWhiteSpace(SubfolderName)
        ? "图片将保存到：每张原图所在目录 / …"
        : $"图片将保存到：每张原图所在目录 / {SubfolderName.Trim()}";
    public string ChooseDirectoryLabel => string.IsNullOrWhiteSpace(CustomDirectory) ? "选择" : "更改";
    public bool IsValid => TryBuild(out _, out _);
    public string? ValidationError { get { TryBuild(out _, out var error); return error; } }
    public bool HasValidationError => !string.IsNullOrWhiteSpace(ValidationError);
    public string LocationSummary => SelectedLocation.Value switch
    {
        OutputLocationMode.SameAsInput => "与原图相同目录",
        OutputLocationMode.Subfolder => $"原图旁 / {SubfolderName}",
        OutputLocationMode.CustomDirectory => CustomDirectory,
        _ => string.Empty
    };
    public string? PickerError
    {
        get => _pickerError;
        private set { if (SetProperty(ref _pickerError, value)) OnPropertyChanged(nameof(HasPickerError)); }
    }
    public bool HasPickerError => !string.IsNullOrWhiteSpace(PickerError);

    public AsyncCommand ChooseDirectoryCommand { get; }
    public RelayCommand<object?> InsertNameTokenCommand { get; }
    public RelayCommand<object?> InsertIndexTokenCommand { get; }

    public void Apply(OutputPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        _selectedLocation = Locations.First(option => option.Value == policy.LocationPolicy.Mode);
        _selectedNaming = NamingModes.First(option => option.Value == policy.NamingPolicy.Mode);
        _selectedOverwrite = OverwritePolicies.First(option => option.Value == policy.OverwritePolicy);
        _subfolderName = policy.LocationPolicy.SubfolderName ?? "AtomPix_Output";
        _customDirectory = policy.LocationPolicy.CustomDirectory ?? string.Empty;
        if (policy.NamingPolicy.Suffix is not null)
        {
            _fileNameSuffix = policy.NamingPolicy.Suffix;
        }
        if (policy.NamingPolicy.Pattern is not null)
        {
            _customFileNamePattern = policy.NamingPolicy.Pattern;
        }
        PickerError = null;
        OnPropertyChanged(string.Empty);
        InsertIndexTokenCommand.NotifyCanExecuteChanged();
        _draftChanged();
    }

    public void SetOverwrite(OverwritePolicy overwritePolicy)
    {
        SelectedOverwrite = OverwritePolicies.First(option => option.Value == overwritePolicy);
    }

    public int InsertTokenAt(string token, int selectionStart, int selectionEnd)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        var start = Math.Clamp(Math.Min(selectionStart, selectionEnd), 0, CustomFileNamePattern.Length);
        var end = Math.Clamp(Math.Max(selectionStart, selectionEnd), start, CustomFileNamePattern.Length);
        CustomFileNamePattern = CustomFileNamePattern[..start] + token + CustomFileNamePattern[end..];
        return start + token.Length;
    }

    public bool TryBuild(out OutputPolicy? policy, out string? error)
    {
        policy = null;
        error = null;
        try
        {
            var location = SelectedLocation.Value switch
            {
                OutputLocationMode.SameAsInput => new OutputLocationPolicy(OutputLocationMode.SameAsInput, null, null),
                OutputLocationMode.Subfolder => new OutputLocationPolicy(OutputLocationMode.Subfolder, null, SubfolderName.Trim()),
                OutputLocationMode.CustomDirectory => new OutputLocationPolicy(OutputLocationMode.CustomDirectory, CustomDirectory.Trim(), null),
                _ => throw new ArgumentOutOfRangeException(nameof(SelectedLocation))
            };
            var naming = SelectedNaming.Value switch
            {
                OutputNamingMode.KeepOriginalName => new OutputNamingPolicy(OutputNamingMode.KeepOriginalName, null),
                OutputNamingMode.AppendSuffix => new OutputNamingPolicy(OutputNamingMode.AppendSuffix, FileNameSuffix.Trim()),
                OutputNamingMode.CustomPattern => new OutputNamingPolicy(OutputNamingMode.CustomPattern, null, CustomFileNamePattern.Trim()),
                _ => throw new ArgumentOutOfRangeException(nameof(SelectedNaming))
            };
            policy = new OutputPolicy(location, naming, SelectedOverwrite.Value);
            return true;
        }
        catch (ArgumentException)
        {
            error = SelectedLocation.Value switch
            {
                OutputLocationMode.Subfolder when string.IsNullOrWhiteSpace(SubfolderName) => "输出子目录名称不能为空。",
                OutputLocationMode.CustomDirectory when string.IsNullOrWhiteSpace(CustomDirectory) => "请选择自定义输出目录。",
                _ when SelectedNaming.Value == OutputNamingMode.AppendSuffix && string.IsNullOrWhiteSpace(FileNameSuffix) => "文件名后缀不能为空。",
                _ when SelectedNaming.Value == OutputNamingMode.CustomPattern && string.IsNullOrWhiteSpace(CustomFileNamePattern) => "文件名格式不能为空。",
                _ => "文件名格式无效，只能使用 {name} 与 {index} 占位符，且不能包含路径字符。"
            };
            return false;
        }
    }

    private async Task ChooseDirectoryAsync(CancellationToken cancellationToken)
    {
        PickerError = null;
        var selection = await _picker.PickFolderAsync(cancellationToken);
        if (selection.Status == DesktopSelectionStatus.Canceled)
        {
            return;
        }

        if (selection.Status != DesktopSelectionStatus.Selected || selection.Paths.Count != 1)
        {
            PickerError = DesktopErrorText.FromPicker(selection.ErrorMessage);
            _feedbackRequested?.Invoke(PickerError);
            return;
        }

        CustomDirectory = selection.Paths[0];
        SelectedLocation = Locations.First(option => option.Value == OutputLocationMode.CustomDirectory);
    }

    private void NotifyDraftChanged()
    {
        PickerError = null;
        OnPropertyChanged(nameof(IsSubfolder));
        OnPropertyChanged(nameof(IsSameAsInput));
        OnPropertyChanged(nameof(IsCustomDirectory));
        OnPropertyChanged(nameof(IsKeepOriginalName));
        OnPropertyChanged(nameof(IsAppendSuffix));
        OnPropertyChanged(nameof(IsCustomPattern));
        OnPropertyChanged(nameof(FileNamePattern));
        OnPropertyChanged(nameof(NamingPreview));
        OnPropertyChanged(nameof(SubfolderDestinationHint));
        OnPropertyChanged(nameof(ChooseDirectoryLabel));
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(ValidationError));
        OnPropertyChanged(nameof(HasValidationError));
        OnPropertyChanged(nameof(LocationSummary));
        _draftChanged();
    }

    private static string ExpandNamingPreview(string pattern) => pattern
        .Replace("{name}", "示例图片", StringComparison.Ordinal)
        .Replace("{index}", "001", StringComparison.Ordinal);
}
