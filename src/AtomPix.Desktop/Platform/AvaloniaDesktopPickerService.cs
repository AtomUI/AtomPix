namespace AtomPix.Desktop.Platform;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

public sealed class AvaloniaDesktopPickerService : IDesktopPickerService
{
    private static readonly FilePickerFileType SupportedImages = new("AtomPix 支持的图片")
    {
        Patterns = ["*.jpg", "*.jpeg", "*.png", "*.webp", "*.bmp", "*.gif", "*.tif", "*.tiff"]
    };

    public Task<DesktopSelectionResult> PickSingleImageAsync(CancellationToken cancellationToken) =>
        PickFilesAsync(allowMultiple: false, cancellationToken);

    public Task<DesktopSelectionResult> PickImagesAsync(CancellationToken cancellationToken) =>
        PickFilesAsync(allowMultiple: true, cancellationToken);

    public async Task<DesktopSelectionResult> PickFolderAsync(CancellationToken cancellationToken)
    {
        var storage = ResolveStorageProvider();
        if (storage is null)
        {
            return DesktopSelectionResult.Unavailable("当前窗口不支持目录选择。");
        }

        try
        {
            var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "打开图片文件夹",
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
            return DesktopSelectionResult.Failed("无法打开系统目录选择器。");
        }
    }

    private static async Task<DesktopSelectionResult> PickFilesAsync(
        bool allowMultiple,
        CancellationToken cancellationToken)
    {
        var storage = ResolveStorageProvider();
        if (storage is null)
        {
            return DesktopSelectionResult.Unavailable("当前窗口不支持文件选择。");
        }

        try
        {
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = allowMultiple ? "添加图片" : "打开图片",
                AllowMultiple = allowMultiple,
                FileTypeFilter = [SupportedImages]
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
            return DesktopSelectionResult.Failed("无法打开系统文件选择器。");
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
