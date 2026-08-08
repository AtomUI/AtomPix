namespace AtomPix.Desktop.ViewModels;

using AtomPix.Core.Errors;
using AtomPix.Desktop.Platform;

public sealed class DiagnosticErrorViewModel : ObservableObject
{
    private readonly IDesktopClipboardService _clipboard;
    private string? _diagnosticId;

    public DiagnosticErrorViewModel(IDesktopClipboardService clipboard)
    {
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        CopyCommand = new AsyncCommand(CopyAsync, () => HasDiagnosticId);
    }

    public string? DiagnosticId
    {
        get => _diagnosticId;
        private set
        {
            if (SetProperty(ref _diagnosticId, value))
            {
                OnPropertyChanged(nameof(HasDiagnosticId));
                CopyCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool HasDiagnosticId => !string.IsNullOrWhiteSpace(DiagnosticId);

    public AsyncCommand CopyCommand { get; }

    public void Set(AtomPixError? error) => DiagnosticId = DesktopErrorText.DiagnosticId(error);

    public void Clear() => DiagnosticId = null;

    private async Task CopyAsync(CancellationToken cancellationToken)
    {
        if (DiagnosticId is { } value) await _clipboard.SetTextAsync(value, cancellationToken);
    }
}
