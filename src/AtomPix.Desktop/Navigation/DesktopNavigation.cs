namespace AtomPix.Desktop.Navigation;

using AtomPix.Core.ValueObjects;
using AtomPix.Imaging.Abstractions.Processing;
using AtomPix.Workflows.Images;

public enum DesktopRoute
{
    Browse,
    Compress,
    Convert,
    Resize,
    Crop,
    Batch,
    Settings
}

public abstract record DesktopNavigationContext;

public sealed record BrowserNavigationContext(
    LocalPath? DirectoryPath,
    IReadOnlyList<BrowserImageCandidate> Items,
    LocalPath? PreferredPath = null,
    ImageProbeResult? PreferredProbe = null) : DesktopNavigationContext;

public sealed record SingleImageNavigationContext(
    LocalPath InputPath,
    ImageProbeResult Probe) : DesktopNavigationContext;

public sealed record BrowserToolNavigationContext(
    BrowserNavigationContext Browser) : DesktopNavigationContext;

public sealed record DesktopNavigationRequest(
    DesktopRoute Route,
    DesktopNavigationContext? Context = null);

public interface IDesktopNavigator
{
    bool IsNavigationLocked { get; }

    bool Navigate(DesktopNavigationRequest request);
}

public interface IDesktopNavigationIntentSource : IDesktopNavigator
{
    public event EventHandler<DesktopNavigationRequest>? NavigationRequested;
}

public interface IDesktopForegroundTaskGate
{
    event EventHandler<bool>? ForegroundTaskStateChanged;

    bool IsForegroundTaskActive { get; }

    bool TryBeginForegroundTask();

    void EndForegroundTask();

    void SetForegroundTaskActive(bool value);
}

public sealed class DesktopForegroundTaskGate : IDesktopForegroundTaskGate
{
    public event EventHandler<bool>? ForegroundTaskStateChanged;

    public bool IsForegroundTaskActive { get; private set; }

    public bool TryBeginForegroundTask()
    {
        if (IsForegroundTaskActive)
        {
            return false;
        }

        SetForegroundTaskActive(true);
        return true;
    }

    public void EndForegroundTask() => SetForegroundTaskActive(false);

    public void SetForegroundTaskActive(bool value)
    {
        if (IsForegroundTaskActive == value)
        {
            return;
        }

        IsForegroundTaskActive = value;
        ForegroundTaskStateChanged?.Invoke(this, value);
    }
}

public sealed class DesktopNavigationIntentHub(
    IDesktopForegroundTaskGate foregroundTasks) : IDesktopNavigationIntentSource
{
    public event EventHandler<DesktopNavigationRequest>? NavigationRequested;

    public bool IsNavigationLocked => foregroundTasks.IsForegroundTaskActive;

    public bool Navigate(DesktopNavigationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (IsNavigationLocked)
        {
            return false;
        }

        NavigationRequested?.Invoke(this, request);
        return true;
    }
}
