namespace AtomPix.Core.Crop;

using AtomPix.Core.Errors;
using AtomPix.Core.Resize;
using AtomPix.Core.Results;

public sealed record CropRectangle
{
    public CropRectangle(int x, int y, int width, int height)
    {
        if (x < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(x), x, "Crop X coordinate cannot be negative.");
        }

        if (y < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(y), y, "Crop Y coordinate cannot be negative.");
        }

        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "Crop width must be greater than zero.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "Crop height must be greater than zero.");
        }

        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public int X { get; }

    public int Y { get; }

    public int Width { get; }

    public int Height { get; }
}

public sealed record CropAspectRatio
{
    public CropAspectRatio(int widthUnits, int heightUnits)
    {
        if (widthUnits <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(widthUnits), widthUnits, "Crop ratio width must be greater than zero.");
        }

        if (heightUnits <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(heightUnits), heightUnits, "Crop ratio height must be greater than zero.");
        }

        var divisor = GreatestCommonDivisor(widthUnits, heightUnits);
        WidthUnits = widthUnits / divisor;
        HeightUnits = heightUnits / divisor;
    }

    public int WidthUnits { get; }

    public int HeightUnits { get; }

    private static int GreatestCommonDivisor(int left, int right)
    {
        while (right != 0)
        {
            (left, right) = (right, left % right);
        }

        return left;
    }
}

public static class CropRules
{
    public static OperationResult<CropRectangle> ValidateCropRectangle(ImageSize inputSize, CropRectangle cropArea)
    {
        ArgumentNullException.ThrowIfNull(cropArea);

        if ((long)cropArea.X + cropArea.Width > inputSize.Width
            || (long)cropArea.Y + cropArea.Height > inputSize.Height)
        {
            return OperationResult<CropRectangle>.Failure(new AtomPixError(
                AtomPixErrorCode.InvalidCropOptions,
                AtomPixErrorCategory.Validation,
                "Crop rectangle must be fully contained within the logical image bounds."));
        }

        return OperationResult<CropRectangle>.Success(cropArea);
    }
}
