namespace AtomPix.Core.Conversion;

using AtomPix.Core.Compression;

public sealed record ConversionProfile
{
    public ConversionProfile(
        OutputImageFormat outputFormat,
        ImageQuality? quality,
        ResizePolicy resizePolicy,
        MetadataPolicy metadataPolicy)
    {
        if (!Enum.IsDefined(outputFormat))
        {
            throw new ArgumentOutOfRangeException(nameof(outputFormat), outputFormat, "Unsupported output image format.");
        }

        if (!Enum.IsDefined(metadataPolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(metadataPolicy), metadataPolicy, "Unsupported metadata policy.");
        }

        OutputFormat = outputFormat;
        Quality = quality;
        ResizePolicy = resizePolicy ?? throw new ArgumentNullException(nameof(resizePolicy));
        MetadataPolicy = metadataPolicy;
    }

    public OutputImageFormat OutputFormat { get; }

    public ImageQuality? Quality { get; }

    public ResizePolicy ResizePolicy { get; }

    public MetadataPolicy MetadataPolicy { get; }

    public static ConversionProfile WebPDefault() =>
        new(OutputImageFormat.WebP, new ImageQuality(80), ResizePolicy.None, MetadataPolicy.Remove);
}

public enum OutputImageFormat
{
    Jpeg,
    Png,
    WebP
}
