namespace AtomPix.Core.Resize;

using AtomPix.Core.Compression;

public readonly record struct ImageSize
{
    public ImageSize(int width, int height)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "Image width must be greater than zero.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "Image height must be greater than zero.");
        }

        Width = width;
        Height = height;
    }

    public int Width { get; }

    public int Height { get; }
}

public sealed record ResolvedResizeSize
{
    public ResolvedResizeSize(int width, int height)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "Resolved width must be greater than zero.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "Resolved height must be greater than zero.");
        }

        Width = width;
        Height = height;
    }

    public int Width { get; }

    public int Height { get; }

    public ImageSize ToImageSize() => new(Width, Height);
}

public abstract record ResizePolicy
{
    private protected ResizePolicy()
    {
    }

    public abstract ResolvedResizeSize Resolve(ImageSize inputSize);

    protected static int RoundAndClamp(decimal value, string parameterName)
    {
        var rounded = decimal.Round(value, 0, MidpointRounding.AwayFromZero);
        if (rounded > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Resolved image dimension exceeds the supported integer range.");
        }

        return Math.Max(1, decimal.ToInt32(rounded));
    }

    protected static int FloorAndClamp(decimal value, string parameterName)
    {
        var floored = decimal.Floor(value);
        if (floored > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Resolved image dimension exceeds the supported integer range.");
        }

        return Math.Max(1, decimal.ToInt32(floored));
    }
}

public sealed record PixelResizePolicy : ResizePolicy
{
    public PixelResizePolicy(int? width, int? height, bool maintainAspectRatio, bool preventUpscaling = false)
    {
        if (width is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be greater than zero when specified.");
        }

        if (height is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be greater than zero when specified.");
        }

        if (maintainAspectRatio && width is null && height is null)
        {
            throw new ArgumentException("Maintaining aspect ratio requires at least one dimension.");
        }

        if (!maintainAspectRatio && (width is null || height is null))
        {
            throw new ArgumentException("Independent pixel resize requires both width and height.");
        }

        Width = width;
        Height = height;
        MaintainAspectRatio = maintainAspectRatio;
        PreventUpscaling = preventUpscaling;
    }

    public int? Width { get; }

    public int? Height { get; }

    public bool MaintainAspectRatio { get; }

    public bool PreventUpscaling { get; }

    public override ResolvedResizeSize Resolve(ImageSize inputSize)
    {
        ResolvedResizeSize resolved;
        if (!MaintainAspectRatio)
        {
            resolved = new ResolvedResizeSize(Width!.Value, Height!.Value);
        }
        else if (Width is { } width && Height is null)
        {
            var resolvedHeight = RoundAndClamp((decimal)inputSize.Height * width / inputSize.Width, nameof(Width));
            resolved = new ResolvedResizeSize(width, resolvedHeight);
        }
        else if (Height is { } height && Width is null)
        {
            var resolvedWidth = RoundAndClamp((decimal)inputSize.Width * height / inputSize.Height, nameof(Height));
            resolved = new ResolvedResizeSize(resolvedWidth, height);
        }
        else
        {
            var scale = Math.Min((decimal)Width!.Value / inputSize.Width, (decimal)Height!.Value / inputSize.Height);
            resolved = new ResolvedResizeSize(
                FloorAndClamp(inputSize.Width * scale, nameof(Width)),
                FloorAndClamp(inputSize.Height * scale, nameof(Height)));
        }

        if (!PreventUpscaling)
        {
            return resolved;
        }

        if (MaintainAspectRatio)
        {
            return resolved.Width > inputSize.Width || resolved.Height > inputSize.Height
                ? new ResolvedResizeSize(inputSize.Width, inputSize.Height)
                : resolved;
        }

        return new ResolvedResizeSize(
            Math.Min(resolved.Width, inputSize.Width),
            Math.Min(resolved.Height, inputSize.Height));
    }
}

public sealed record PercentageResizePolicy : ResizePolicy
{
    public PercentageResizePolicy(decimal percentage)
    {
        if (percentage <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(percentage), percentage, "Resize percentage must be greater than zero.");
        }

        Percentage = percentage;
    }

    public decimal Percentage { get; }

    public override ResolvedResizeSize Resolve(ImageSize inputSize)
    {
        try
        {
            var scale = Percentage / 100m;
            return new ResolvedResizeSize(
                RoundAndClamp(inputSize.Width * scale, nameof(Percentage)),
                RoundAndClamp(inputSize.Height * scale, nameof(Percentage)));
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(nameof(Percentage), Percentage, "Resize percentage produces dimensions outside the supported range.");
        }
    }
}

public sealed record SameFormatEncodingPolicy
{
    public SameFormatEncodingPolicy(ImageQuality lossyQuality, MetadataPolicy metadataPolicy)
    {
        if (!Enum.IsDefined(metadataPolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(metadataPolicy), metadataPolicy, "Unsupported metadata policy.");
        }

        LossyQuality = lossyQuality;
        MetadataPolicy = metadataPolicy;
    }

    public ImageQuality LossyQuality { get; }

    public MetadataPolicy MetadataPolicy { get; }

    public static SameFormatEncodingPolicy Default { get; } = new(new ImageQuality(90), MetadataPolicy.Remove);
}
