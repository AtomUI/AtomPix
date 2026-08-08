namespace AtomPix.Desktop.Platform;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

public sealed class AvaloniaDesktopLauncherService : IDesktopLauncherService
{
    public async Task<bool> OpenDirectoryAsync(string directoryPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            return false;
        }

        var window = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        var launcher = window?.Launcher;
        if (launcher is null)
        {
            return false;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directoryUri = new UriBuilder
            {
                Scheme = Uri.UriSchemeFile,
                Path = Path.GetFullPath(directoryPath)
            }.Uri;
            return await launcher.LaunchUriAsync(directoryUri);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }
}
