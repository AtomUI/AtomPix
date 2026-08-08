namespace AtomPix.Workflows.Tests;

using ImageMagick;
using AtomPix.Core.Compression;
using AtomPix.Core.Conversion;
using AtomPix.Core.Resize;
using AtomPix.Core.ValueObjects;
using AtomPix.Imaging.Abstractions.Formats;
using AtomPix.Imaging.Abstractions.Processing;
using AtomPix.Imaging.Magick.Processing;

public sealed class VisualOutputArtifactTests
{
    private static readonly object AssetLock = new();
    private readonly string _repoRoot;
    private readonly string _assetDirectory;
    private readonly string _outputDirectory;
    private readonly MagickImageProcessor _processor = new();

    public VisualOutputArtifactTests()
    {
        _repoRoot = FindRepositoryRoot();
        _assetDirectory = Path.Combine(_repoRoot, "tests", "TestAssets", "Images");
        _outputDirectory = Path.Combine(_repoRoot, "tests", "TestOutputs", "Images");
        Directory.CreateDirectory(_assetDirectory);
        Directory.CreateDirectory(_outputDirectory);
        EnsureAssets();
    }

    [Fact]
    public async Task Visual_output_artifacts_are_generated_for_manual_review()
    {
        var balanced = OutputPath("compressed-balanced.jpg");
        var maximum = OutputPath("compressed-maximum.jpg");
        var resized = OutputPath("resized-compressed.jpg");
        var alphaWebp = OutputPath("converted-png-alpha-to-webp.webp");
        var alphaJpeg = OutputPath("converted-png-alpha-to-jpeg.jpg");
        var webpJpeg = OutputPath("converted-webp-to-jpeg.jpg");
        var jpegPng = OutputPath("converted-jpeg-to-png.png");

        var balancedResult = await _processor.CompressAsync(
            new ImageCompressRequest(AssetPath("jpeg-detailed.jpg"), balanced, CompressionProfile.BalancedDefault()),
            CancellationToken.None);
        var maximumResult = await _processor.CompressAsync(
            new ImageCompressRequest(AssetPath("jpeg-detailed.jpg"), maximum, CompressionProfile.MaximumDefault()),
            CancellationToken.None);
        var resizedResult = await _processor.ResizeAsync(
            new ImageResizeRequest(
                AssetPath("jpeg-detailed.jpg"),
                resized,
                new ResolvedResizeSize(120, 80),
                SameFormatEncodingPolicy.Default),
            CancellationToken.None);
        var alphaWebpResult = await _processor.ConvertAsync(
            new ImageConvertRequest(AssetPath("png-alpha.png"), alphaWebp, ConversionProfile.WebPDefault()),
            CancellationToken.None);
        var alphaJpegResult = await _processor.ConvertAsync(
            new ImageConvertRequest(
                AssetPath("png-alpha.png"),
                alphaJpeg,
                new ConversionProfile(OutputImageFormat.Jpeg, new ImageQuality(82), MetadataPolicy.Remove, TransparencyPolicy.Default)),
            CancellationToken.None);
        var webpJpegResult = await _processor.ConvertAsync(
            new ImageConvertRequest(
                AssetPath("webp-basic.webp"),
                webpJpeg,
                new ConversionProfile(OutputImageFormat.Jpeg, new ImageQuality(82), MetadataPolicy.Remove, TransparencyPolicy.Default)),
            CancellationToken.None);
        var jpegPngResult = await _processor.ConvertAsync(
            new ImageConvertRequest(
                AssetPath("jpeg-basic.jpg"),
                jpegPng,
                new ConversionProfile(OutputImageFormat.Png, null, MetadataPolicy.Remove, TransparencyPolicy.Default)),
            CancellationToken.None);

        Assert.True(balancedResult.Succeeded, balancedResult.Error?.Message);
        Assert.True(maximumResult.Succeeded, maximumResult.Error?.Message);
        Assert.True(resizedResult.Succeeded, resizedResult.Error?.Message);
        Assert.True(alphaWebpResult.Succeeded, alphaWebpResult.Error?.Message);
        Assert.True(alphaJpegResult.Succeeded, alphaJpegResult.Error?.Message);
        Assert.True(webpJpegResult.Succeeded, webpJpegResult.Error?.Message);
        Assert.True(jpegPngResult.Succeeded, jpegPngResult.Error?.Message);

        Assert.True(maximumResult.Value!.OutputSizeBytes < balancedResult.Value!.OutputSizeBytes);
        AssertImage(balanced, MagickFormat.Jpeg);
        AssertImage(maximum, MagickFormat.Jpeg);
        using (var image = new MagickImage(resized.Value))
        {
            Assert.Equal(MagickFormat.Jpeg, image.Format);
            Assert.True(image.Width <= 120);
            Assert.True(image.Height <= 80);
        }

        using (var image = new MagickImage(alphaWebp.Value))
        {
            Assert.Equal(MagickFormat.WebP, image.Format);
            Assert.True(image.HasAlpha);
        }

        using (var image = new MagickImage(alphaJpeg.Value))
        {
            Assert.Equal(MagickFormat.Jpeg, image.Format);
            Assert.False(image.HasAlpha);
        }

        AssertImage(webpJpeg, MagickFormat.Jpeg);
        AssertImage(jpegPng, MagickFormat.Png);
        WriteOutputManifest();
    }

    private LocalPath AssetPath(string fileName) => new(Path.Combine(_assetDirectory, fileName));

    private LocalPath OutputPath(string fileName) => new(Path.Combine(_outputDirectory, fileName));

    private static void AssertImage(LocalPath path, MagickFormat expectedFormat)
    {
        Assert.True(File.Exists(path.Value), $"Expected image was not written: {path.Value}");
        using var image = new MagickImage(path.Value);
        Assert.Equal(expectedFormat, image.Format);
        Assert.True(image.Width > 0);
        Assert.True(image.Height > 0);
    }

    private void EnsureAssets()
    {
        lock (AssetLock)
        {
            if (!File.Exists(AssetPath("jpeg-basic.jpg").Value))
            {
                using var image = new MagickImage(MagickColors.Red, 160, 120) { Format = MagickFormat.Jpeg, Quality = 90 };
                image.Write(AssetPath("jpeg-basic.jpg").Value);
            }

            if (!File.Exists(AssetPath("jpeg-detailed.jpg").Value))
            {
                using var image = new MagickImage(MagickColors.White, 320, 240) { Format = MagickFormat.Jpeg, Quality = 96 };
                var pixels = image.GetPixels();
                for (var y = 0; y < 240; y++)
                {
                    for (var x = 0; x < 320; x++)
                    {
                        var red = (byte)(x % 256);
                        var green = (byte)(y % 256);
                        var blue = (byte)((x * y) % 256);
                        pixels.SetPixel(x, y, [red, green, blue]);
                    }
                }

                image.Write(AssetPath("jpeg-detailed.jpg").Value);
            }

            if (!File.Exists(AssetPath("png-alpha.png").Value))
            {
                using var image = new MagickImage(MagickColors.Transparent, 160, 120) { Format = MagickFormat.Png };
                var pixels = image.GetPixels();
                for (var y = 20; y < 100; y++)
                {
                    for (var x = 20; x < 140; x++)
                    {
                        var alpha = (byte)(80 + (x + y) % 176);
                        pixels.SetPixel(x, y, [0, 120, 255, alpha]);
                    }
                }

                pixels.SetPixel(12, 12, [255, 0, 0, 255]);
                image.Write(AssetPath("png-alpha.png").Value);
            }

            if (!File.Exists(AssetPath("webp-basic.webp").Value))
            {
                using var image = new MagickImage(MagickColors.Blue, 160, 120) { Format = MagickFormat.WebP, Quality = 82 };
                image.Write(AssetPath("webp-basic.webp").Value);
            }
        }
    }

    private void WriteOutputManifest()
    {
        var manifest = """
            # AtomPix Visual Test Outputs

            These images are generated by `VisualOutputArtifactTests` and intentionally kept for manual inspection.

            | File | Source | Operation |
            | --- | --- | --- |
            | `compressed-balanced.jpg` | `jpeg-detailed.jpg` | Balanced JPEG compression |
            | `compressed-maximum.jpg` | `jpeg-detailed.jpg` | Maximum JPEG compression |
            | `resized-compressed.jpg` | `jpeg-detailed.jpg` | Balanced JPEG compression, resize within 120x80 |
            | `converted-png-alpha-to-webp.webp` | `png-alpha.png` | PNG alpha to WebP |
            | `converted-png-alpha-to-jpeg.jpg` | `png-alpha.png` | PNG alpha to JPEG |
            | `converted-webp-to-jpeg.jpg` | `webp-basic.webp` | WebP to JPEG |
            | `converted-jpeg-to-png.png` | `jpeg-basic.jpg` | JPEG to PNG |
            """;
        File.WriteAllText(Path.Combine(_outputDirectory, "README.md"), manifest);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AtomPix.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
