namespace AtomPix.Core.Conversion;

using AtomPix.Core.Compression;

public sealed record ConversionProfile
{
    public ConversionProfile(
        OutputImageFormat outputFormat,
        ImageQuality? quality,
        MetadataPolicy metadataPolicy,
        TransparencyPolicy transparencyPolicy)
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
        MetadataPolicy = metadataPolicy;
        TransparencyPolicy = transparencyPolicy ?? throw new ArgumentNullException(nameof(transparencyPolicy));
    }

    public OutputImageFormat OutputFormat { get; }

    public ImageQuality? Quality { get; }

    public MetadataPolicy MetadataPolicy { get; }

    public TransparencyPolicy TransparencyPolicy { get; }

    public static ConversionProfile WebPDefault() =>
        new(OutputImageFormat.WebP, new ImageQuality(80), MetadataPolicy.Remove, TransparencyPolicy.Default);
}

public sealed record RgbColor(byte Red, byte Green, byte Blue)
{
    public static RgbColor White { get; } = new(255, 255, 255);

    public static RgbColor Black { get; } = new(0, 0, 0);

    public string ToHexString() => $"#{Red:X2}{Green:X2}{Blue:X2}";

    public override string ToString() => ToHexString();

    public static RgbColor Parse(string value)
    {
        if (!TryParse(value, out var color))
        {
            throw new FormatException("RGB color must use the #RRGGBB format.");
        }

        return color;
    }

    public static bool TryParse(string? value, out RgbColor color)
    {
        color = Black;
        if (value is null || value.Length != 7 || value[0] != '#')
        {
            return false;
        }

        if (!byte.TryParse(value.AsSpan(1, 2), System.Globalization.NumberStyles.HexNumber, null, out var red)
            || !byte.TryParse(value.AsSpan(3, 2), System.Globalization.NumberStyles.HexNumber, null, out var green)
            || !byte.TryParse(value.AsSpan(5, 2), System.Globalization.NumberStyles.HexNumber, null, out var blue))
        {
            return false;
        }

        color = new RgbColor(red, green, blue);
        return true;
    }
}

public sealed record TransparencyPolicy
{
    public TransparencyPolicy(RgbColor opaqueBackgroundColor)
    {
        OpaqueBackgroundColor = opaqueBackgroundColor ?? throw new ArgumentNullException(nameof(opaqueBackgroundColor));
    }

    public RgbColor OpaqueBackgroundColor { get; }

    public static TransparencyPolicy Default { get; } = new(RgbColor.White);
}

public enum OutputImageFormat
{
    Jpeg,
    Png,
    WebP
}
