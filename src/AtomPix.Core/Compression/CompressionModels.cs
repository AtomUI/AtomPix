namespace AtomPix.Core.Compression;

public sealed record CompressionProfile
{
    public CompressionProfile(
        CompressionMode mode,
        ImageQuality? quality,
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

        if (mode == CompressionMode.Smart && quality is not null)
        {
            throw new ArgumentException("Smart compression cannot carry an explicit quality.", nameof(quality));
        }

        Mode = mode;
        Quality = quality;
        MetadataPolicy = metadataPolicy;
    }

    public CompressionMode Mode { get; }

    public ImageQuality? Quality { get; }

    public MetadataPolicy MetadataPolicy { get; }

    public static CompressionProfile SmartDefault() =>
        new(CompressionMode.Smart, null, MetadataPolicy.Remove);

    public static CompressionProfile HighQualityDefault() =>
        new(CompressionMode.HighQuality, new ImageQuality(90), MetadataPolicy.Remove);

    public static CompressionProfile BalancedDefault() =>
        new(CompressionMode.Balanced, new ImageQuality(80), MetadataPolicy.Remove);

    public static CompressionProfile MaximumDefault() =>
        new(CompressionMode.Maximum, new ImageQuality(65), MetadataPolicy.Remove);
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

public enum MetadataPolicy
{
    Preserve,
    Remove
}
