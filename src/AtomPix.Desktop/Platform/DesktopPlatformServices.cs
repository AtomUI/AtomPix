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
}

public interface IDesktopClipboardService
{
    Task<bool> SetTextAsync(string text, CancellationToken cancellationToken);
}

public interface IDesktopDispatcher
{
    void Post(Action action);
}

public enum DesktopFeedbackSeverity
{
    Information,
    Success,
    Warning,
    Error
}

public sealed record DesktopNotificationRequest(
    string Title,
    string Content,
    DesktopFeedbackSeverity Severity,
    TimeSpan Expiration,
    Action? OnClick = null);

/// <summary>
/// Window-level feedback boundary. View models publish semantic feedback without
/// owning AtomUI controls or depending on a particular overlay host.
/// </summary>
public interface IDesktopFeedbackService
{
    void ShowMessage(
        string message,
        DesktopFeedbackSeverity severity = DesktopFeedbackSeverity.Information,
        TimeSpan? expiration = null);

    void ShowNotification(DesktopNotificationRequest request);
}
