namespace AtomPix.Desktop.Platform;

using AtomPix.Desktop.Localization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

public sealed class AvaloniaDesktopPickerService : IDesktopPickerService
{
    private readonly IAtomPixLocalizedText _text;
    private readonly FilePickerFileType _supportedImages;

    public AvaloniaDesktopPickerService(IAtomPixLocalizedText text)
    {
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _supportedImages = new FilePickerFileType(_text.GetString("Common.Picker.SupportedImages"))
        {
            Patterns = ["*.jpg", "*.jpeg", "*.png", "*.webp", "*.bmp", "*.gif", "*.tif", "*.tiff"]
        };
    }

    public Task<DesktopSelectionResult> PickSingleImageAsync(CancellationToken cancellationToken) =>
        PickFilesAsync(allowMultiple: false, cancellationToken);

    public Task<DesktopSelectionResult> PickImagesAsync(CancellationToken cancellationToken) =>
        PickFilesAsync(allowMultiple: true, cancellationToken);

    public async Task<DesktopSelectionResult> PickFolderAsync(CancellationToken cancellationToken)
    {
        var storage = ResolveStorageProvider();
        if (storage is null)
        {
            return DesktopSelectionResult.Unavailable(_text.GetString("Common.Picker.FolderUnavailable"));
        }

        try
        {
            var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = _text.GetString("Common.Picker.OpenImageFolder"),
                AllowMultiple = false
            });
            cancellationToken.ThrowIfCancellationRequested();
            return ToResult(folders);
        }
        catch (OperationCanceledException)
        {
            return DesktopSelectionResult.Canceled();
        }
        catch
        {
            return DesktopSelectionResult.Failed(_text.GetString("Common.Picker.FolderFailed"));
        }
    }

    private async Task<DesktopSelectionResult> PickFilesAsync(
        bool allowMultiple,
        CancellationToken cancellationToken)
    {
        var storage = ResolveStorageProvider();
        if (storage is null)
        {
            return DesktopSelectionResult.Unavailable(_text.GetString("Common.Picker.FileUnavailable"));
        }

        try
        {
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = _text.GetString(allowMultiple ? "Common.Picker.AddImages" : "Common.Picker.OpenImage"),
                AllowMultiple = allowMultiple,
                FileTypeFilter = [_supportedImages]
            });
            cancellationToken.ThrowIfCancellationRequested();
            return ToResult(files);
        }
        catch (OperationCanceledException)
        {
            return DesktopSelectionResult.Canceled();
        }
        catch
        {
            return DesktopSelectionResult.Failed(_text.GetString("Common.Picker.FileFailed"));
        }
    }

    private static IStorageProvider? ResolveStorageProvider()
    {
        var window = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        return window?.StorageProvider;
    }

    private static DesktopSelectionResult ToResult(IEnumerable<IStorageItem> items)
    {
        var paths = items
            .Select(item => item.TryGetLocalPath())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .ToArray();

        return paths.Length == 0
            ? DesktopSelectionResult.Canceled()
            : new DesktopSelectionResult(DesktopSelectionStatus.Selected, paths);
    }
}
