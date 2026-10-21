namespace AtomPix.Desktop.ViewModels;

using AtomPix.Core.Output;
using AtomPix.Desktop.Localization;
using AtomPix.Desktop.Platform;
using AtomUI.City.Presentation;

public sealed class OutputPolicyEditorViewModel : ObservableObject
{
    private readonly IDesktopPickerService _picker;
    private readonly IAtomPixLocalizedText _text;
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
        IAtomPixLocalizedText text,
        Action? draftChanged = null,
        Action<string>? feedbackRequested = null,
        IUiCommandFactory? commandFactory = null)
    {
        _picker = picker ?? throw new ArgumentNullException(nameof(picker));
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _draftChanged = draftChanged ?? (() => { });
        _feedbackRequested = feedbackRequested;
        Locations =
        [
            new(_text.GetString("Common.Option.OutputLocation.SubfolderShort"), OutputLocationMode.Subfolder),
            new(_text.GetString("Common.Option.OutputLocation.SameAsInputShort"), OutputLocationMode.SameAsInput),
            new(_text.GetString("Common.Option.OutputLocation.CustomDirectory"), OutputLocationMode.CustomDirectory)
        ];
        OverwritePolicies =
        [
            new(_text.GetString("Common.Option.Overwrite.AutoRename"), OverwritePolicy.AutoRename),
            new(_text.GetString("Common.Option.Overwrite.Skip"), OverwritePolicy.Skip),
            new(_text.GetString("Common.Option.Overwrite.Overwrite"), OverwritePolicy.Overwrite)
        ];
        NamingModes =
        [
            new(_text.GetString("Common.Option.Naming.KeepOriginal"), OutputNamingMode.KeepOriginalName),
            new(_text.GetString("Common.Option.Naming.AppendSuffix"), OutputNamingMode.AppendSuffix),
            new(_text.GetString("Common.Option.Naming.CustomPattern"), OutputNamingMode.CustomPattern)
        ];
        _selectedLocation = Locations[0];
        _selectedNaming = NamingModes[1];
        _selectedOverwrite = OverwritePolicies[0];
        var commands = commandFactory ?? HeadlessUiCommandFactory.Instance;
        ChooseDirectoryCommand = commands.CreateAsync(ChooseDirectoryAsync);
        InsertNameTokenCommand = commands.Create(() => CustomFileNamePattern += "{name}");
        InsertIndexTokenCommand = commands.Create(
            () => CustomFileNamePattern += "{index}",
            () => !CustomFileNamePattern.Contains("{index}", StringComparison.Ordinal));
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
        ? _text.GetString("Common.Output.SubfolderDestinationEmptyHint")
        : _text.Format("Common.Output.SubfolderDestinationHint", SubfolderName.Trim());
    public string ChooseDirectoryLabel => string.IsNullOrWhiteSpace(CustomDirectory)
        ? _text.GetString("Common.Action.Choose")
        : _text.GetString("Common.Action.Change");
    public bool IsValid => TryBuild(out _, out _);
    public string? ValidationError { get { TryBuild(out _, out var error); return error; } }
    public bool HasValidationError => !string.IsNullOrWhiteSpace(ValidationError);
    public string LocationSummary => SelectedLocation.Value switch
    {
        OutputLocationMode.SameAsInput => _text.GetString("Common.Option.OutputLocation.SameAsInputLong"),
        OutputLocationMode.Subfolder => _text.Format("Common.Output.LocationSummary.Subfolder", SubfolderName),
        OutputLocationMode.CustomDirectory => CustomDirectory,
        _ => string.Empty
    };
    public string? PickerError
    {
        get => _pickerError;
        private set { if (SetProperty(ref _pickerError, value)) OnPropertyChanged(nameof(HasPickerError)); }
    }
    public bool HasPickerError => !string.IsNullOrWhiteSpace(PickerError);

    public IAsyncRelayCommand ChooseDirectoryCommand { get; }
    public IRelayCommand InsertNameTokenCommand { get; }
    public IRelayCommand InsertIndexTokenCommand { get; }

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
                OutputLocationMode.Subfolder when string.IsNullOrWhiteSpace(SubfolderName) => _text.GetString("Common.Validation.Output.SubfolderRequired"),
                OutputLocationMode.CustomDirectory when string.IsNullOrWhiteSpace(CustomDirectory) => _text.GetString("Common.Validation.Output.CustomDirectoryRequired"),
                _ when SelectedNaming.Value == OutputNamingMode.AppendSuffix && string.IsNullOrWhiteSpace(FileNameSuffix) => _text.GetString("Common.Validation.Output.SuffixRequired"),
                _ when SelectedNaming.Value == OutputNamingMode.CustomPattern && string.IsNullOrWhiteSpace(CustomFileNamePattern) => _text.GetString("Common.Validation.Output.PatternRequired"),
                _ => _text.GetString("Common.Validation.Output.PatternInvalid")
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
            PickerError = DesktopErrorText.FromPicker(selection.ErrorMessage, _text);
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

    private string ExpandNamingPreview(string pattern) => pattern
        .Replace("{name}", _text.GetString("Common.Output.ExampleImageName"), StringComparison.Ordinal)
        .Replace("{index}", "001", StringComparison.Ordinal);
}
