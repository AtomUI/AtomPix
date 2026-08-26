namespace AtomPix.Desktop.ViewModels;

using AtomUI.Labs.Controls.ImageGallery;

/// <summary>
/// Desktop-only adapter between AtomPix browser items and AtomUI.Labs ImageGallery.
/// The normalized path is the stable item/source identity for the lifetime of a browser session.
/// </summary>
public sealed class ImageGalleryItemAdapter : IImageGalleryItem
{
    public ImageGalleryItemAdapter(BrowserItemViewModel item)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));
        Key = Path.GetFullPath(item.Path.Value);
        Title = item.DisplayName;
        MainImageSource = ImageGallerySources.FromFile(item.Path.Value, Key);
    }

    public BrowserItemViewModel Item { get; }

    public object Key { get; }

    public string? Title { get; }

    public IImageGallerySource MainImageSource { get; }

    // A null thumbnail source deliberately reuses MainImageSource with a thumbnail request.
    public IImageGallerySource? ThumbnailImageSource => null;
}
