namespace AtomPix.Desktop.ViewModels;

using AtomPix.Core.Output;
using AtomPix.Desktop.Platform;

public sealed class OutputPolicyEditorViewModel : ObservableObject
{
    private readonly IDesktopPickerService _picker;
    private readonly Action _draftChanged;
    private DesktopChoiceOption<OutputLocationMode> _selectedLocation;
    private DesktopChoiceOption<OverwritePolicy> _selectedOverwrite;
    private string _subfolderName = "AtomPix_Output";
    private string _customDirectory = string.Empty;
    private string _fileNamePattern = "{name}_atompix";
    private string? _pickerError;

    public OutputPolicyEditorViewModel(IDesktopPickerService picker, Action? draftChanged = null)
    {
        _picker = picker ?? throw new ArgumentNullException(nameof(picker));
        _draftChanged = draftChanged ?? (() => { });
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
        _selectedLocation = Locations[0];
        _selectedOverwrite = OverwritePolicies[0];
        ChooseDirectoryCommand = new AsyncCommand(ChooseDirectoryAsync);
        InsertNameTokenCommand = new RelayCommand<object?>(_ => FileNamePattern += "{name}");
        InsertIndexTokenCommand = new RelayCommand<object?>(
            _ => FileNamePattern += "{index}",
            _ => !FileNamePattern.Contains("{index}", StringComparison.Ordinal));
    }

    public IReadOnlyList<DesktopChoiceOption<OutputLocationMode>> Locations { get; }
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

    public string FileNamePattern
    {
        get => _fileNamePattern;
        set
        {
            if (SetProperty(ref _fileNamePattern, value ?? string.Empty))
            {
                InsertIndexTokenCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(CanInsertIndexToken));
                NotifyDraftChanged();
            }
        }
    }

    public bool CanInsertIndexToken => !FileNamePattern.Contains("{index}", StringComparison.Ordinal);

    public bool IsSubfolder => SelectedLocation.Value == OutputLocationMode.Subfolder;
    public bool IsCustomDirectory => SelectedLocation.Value == OutputLocationMode.CustomDirectory;
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
        _selectedOverwrite = OverwritePolicies.First(option => option.Value == policy.OverwritePolicy);
        _subfolderName = policy.LocationPolicy.SubfolderName ?? "AtomPix_Output";
        _customDirectory = policy.LocationPolicy.CustomDirectory ?? string.Empty;
        _fileNamePattern = policy.NamingPolicy.GetBasePattern();
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
        var start = Math.Clamp(Math.Min(selectionStart, selectionEnd), 0, FileNamePattern.Length);
        var end = Math.Clamp(Math.Max(selectionStart, selectionEnd), start, FileNamePattern.Length);
        FileNamePattern = FileNamePattern[..start] + token + FileNamePattern[end..];
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
            var naming = new OutputNamingPolicy(OutputNamingMode.CustomPattern, null, FileNamePattern.Trim());
            policy = new OutputPolicy(location, naming, SelectedOverwrite.Value);
            return true;
        }
        catch (ArgumentException)
        {
            error = SelectedLocation.Value switch
            {
                OutputLocationMode.Subfolder when string.IsNullOrWhiteSpace(SubfolderName) => "输出子目录名称不能为空。",
                OutputLocationMode.CustomDirectory when string.IsNullOrWhiteSpace(CustomDirectory) => "请选择自定义输出目录。",
                _ when string.IsNullOrWhiteSpace(FileNamePattern) => "文件名格式不能为空。",
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
            return;
        }

        CustomDirectory = selection.Paths[0];
        SelectedLocation = Locations.First(option => option.Value == OutputLocationMode.CustomDirectory);
    }

    private void NotifyDraftChanged()
    {
        PickerError = null;
        OnPropertyChanged(nameof(IsSubfolder));
        OnPropertyChanged(nameof(IsCustomDirectory));
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(ValidationError));
        OnPropertyChanged(nameof(HasValidationError));
        OnPropertyChanged(nameof(LocationSummary));
        _draftChanged();
    }
}
