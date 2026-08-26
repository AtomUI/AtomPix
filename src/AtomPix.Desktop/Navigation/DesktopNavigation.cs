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

public sealed class DesktopNavigationCoordinator : IDesktopNavigator
{
    public event EventHandler<DesktopNavigationRequest>? NavigationRequested;

    public event EventHandler<bool>? NavigationLockChanged;

    public bool IsNavigationLocked { get; private set; }

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

    public bool TryBeginForegroundTask()
    {
        if (IsNavigationLocked)
        {
            return false;
        }

        SetNavigationLocked(true);
        return true;
    }

    public void EndForegroundTask() => SetNavigationLocked(false);

    public void SetNavigationLocked(bool value)
    {
        if (IsNavigationLocked == value)
        {
            return;
        }

        IsNavigationLocked = value;
        NavigationLockChanged?.Invoke(this, value);
    }
}
