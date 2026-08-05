namespace AtomPix.Core.Compression;

public sealed record CompressionProfile
{
    public CompressionProfile(
        CompressionMode mode,
        ImageQuality? quality,
        ResizePolicy resizePolicy,
        MetadataPolicy metadataPolicy)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported compression mode.");
        }

        if (!Enum.IsDefined(metadataPolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(metadataPolicy), metadataPolicy, "Unsupported metadata policy.");
        }

        if (mode == CompressionMode.Custom && quality is null)
        {
            throw new ArgumentException("Custom compression requires an explicit quality.", nameof(quality));
        }

        Mode = mode;
        Quality = quality;
        ResizePolicy = resizePolicy ?? throw new ArgumentNullException(nameof(resizePolicy));
        MetadataPolicy = metadataPolicy;
    }

    public CompressionMode Mode { get; }

    public ImageQuality? Quality { get; }

    public ResizePolicy ResizePolicy { get; }

    public MetadataPolicy MetadataPolicy { get; }

    public static CompressionProfile SmartDefault() =>
        new(CompressionMode.Smart, null, ResizePolicy.None, MetadataPolicy.Remove);

    public static CompressionProfile HighQualityDefault() =>
        new(CompressionMode.HighQuality, new ImageQuality(90), ResizePolicy.None, MetadataPolicy.Preserve);

    public static CompressionProfile BalancedDefault() =>
        new(CompressionMode.Balanced, new ImageQuality(80), ResizePolicy.None, MetadataPolicy.Remove);

    public static CompressionProfile MaximumDefault() =>
        new(CompressionMode.Maximum, new ImageQuality(65), ResizePolicy.None, MetadataPolicy.Remove);
}

public enum CompressionMode
{
    Smart,
    HighQuality,
    Balanced,
    Maximum,
    Custom
}

public readonly record struct ImageQuality
{
    public ImageQuality(int value)
    {
        if (value is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Image quality must be between 1 and 100.");
        }

        Value = value;
    }

    public int Value { get; }
}

public sealed record ResizePolicy
{
    public ResizePolicy(ResizeMode mode, int? maxWidth, int? maxHeight, int? percentage)
    {
        switch (mode)
        {
            case ResizeMode.None:
                if (maxWidth is not null || maxHeight is not null || percentage is not null)
                {
                    throw new ArgumentException("None resize mode cannot carry dimensions or percentage.");
                }
                break;
            case ResizeMode.FitWithinBounds:
                if (maxWidth is null && maxHeight is null)
                {
                    throw new ArgumentException("FitWithinBounds requires at least one bound.");
                }
                if (maxWidth is <= 0 || maxHeight is <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(maxWidth), "Bounds must be greater than zero when specified.");
                }
                if (percentage is not null)
                {
                    throw new ArgumentException("FitWithinBounds cannot carry percentage.");
                }
                break;
            case ResizeMode.Percentage:
                if (percentage is null or <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(percentage), "Percentage resize requires a positive percentage.");
                }
                if (maxWidth is not null || maxHeight is not null)
                {
                    throw new ArgumentException("Percentage resize cannot carry bounds.");
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported resize mode.");
        }

        Mode = mode;
        MaxWidth = maxWidth;
        MaxHeight = maxHeight;
        Percentage = percentage;
    }

    public ResizeMode Mode { get; }

    public int? MaxWidth { get; }

    public int? MaxHeight { get; }

    public int? Percentage { get; }

    public static ResizePolicy None { get; } = new(ResizeMode.None, null, null, null);

    public static ResizePolicy FitWithinBounds(int? maxWidth, int? maxHeight) => new(ResizeMode.FitWithinBounds, maxWidth, maxHeight, null);

    public static ResizePolicy ScaleByPercentage(int percentage) => new(ResizeMode.Percentage, null, null, percentage);
}

public enum ResizeMode
{
    None,
    FitWithinBounds,
    Percentage
}

public enum MetadataPolicy
{
    Preserve,
    Remove
}
