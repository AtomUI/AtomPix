namespace AtomPix.Desktop.Platform;

public enum DesktopSelectionStatus
{
    Selected,
    Canceled,
    Unavailable,
    Failed
}

public sealed record DesktopSelectionResult(
    DesktopSelectionStatus Status,
    IReadOnlyList<string> Paths,
    string? ErrorMessage = null)
{
    public static DesktopSelectionResult Selected(params string[] paths) =>
        new(DesktopSelectionStatus.Selected, paths);

    public static DesktopSelectionResult Canceled() =>
        new(DesktopSelectionStatus.Canceled, Array.Empty<string>());

    public static DesktopSelectionResult Unavailable(string message) =>
        new(DesktopSelectionStatus.Unavailable, Array.Empty<string>(), message);

    public static DesktopSelectionResult Failed(string message) =>
        new(DesktopSelectionStatus.Failed, Array.Empty<string>(), message);
}

public interface IDesktopPickerService
{
    Task<DesktopSelectionResult> PickSingleImageAsync(CancellationToken cancellationToken);

    Task<DesktopSelectionResult> PickImagesAsync(CancellationToken cancellationToken);

    Task<DesktopSelectionResult> PickFolderAsync(CancellationToken cancellationToken);
}

public interface IDesktopLauncherService
{
    Task<bool> OpenDirectoryAsync(string directoryPath, CancellationToken cancellationToken);
}

public interface IDesktopDialogService
{
    Task<bool> ConfirmAsync(
        string title,
        string message,
        string confirmText,
        string cancelText,
        CancellationToken cancellationToken);

    Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken);

    Task ShowInformationAsync(string title, string message, CancellationToken cancellationToken);

    Task<UnsavedChangesChoice> ChooseUnsavedChangesAsync(CancellationToken cancellationToken);
}

public enum UnsavedChangesChoice
{
    Save,
    Discard,
    Stay
}

public interface IDesktopClipboardService
{
    Task<bool> SetTextAsync(string text, CancellationToken cancellationToken);
}

public interface IDesktopAppearanceService
{
    void Apply(AtomPix.Core.Settings.ThemeMode themeMode);
}

public interface IDesktopDispatcher
{
    void Post(Action action);
}
