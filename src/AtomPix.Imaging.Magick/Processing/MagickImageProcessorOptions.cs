namespace AtomPix.Imaging.Magick.Processing;

using AtomPix.Imaging.Abstractions.Processing;
using ImageMagick;

public sealed record MagickImageProcessorOptions
{
    public MagickImageProcessorOptions(
        ImageResourceCapabilities resources,
        ulong memoryLimitBytes,
        ulong mapLimitBytes,
        ulong diskLimitBytes,
        int threadLimit,
        string pixelCacheDirectory)
    {
        Resources = resources ?? throw new ArgumentNullException(nameof(resources));
        if (memoryLimitBytes == 0) throw new ArgumentOutOfRangeException(nameof(memoryLimitBytes));
        if (mapLimitBytes == 0) throw new ArgumentOutOfRangeException(nameof(mapLimitBytes));
        if (diskLimitBytes == 0) throw new ArgumentOutOfRangeException(nameof(diskLimitBytes));
        if (threadLimit <= 0) throw new ArgumentOutOfRangeException(nameof(threadLimit));
        if (string.IsNullOrWhiteSpace(pixelCacheDirectory)) throw new ArgumentException("Pixel cache directory cannot be empty.", nameof(pixelCacheDirectory));

        Resources = resources;
        MemoryLimitBytes = memoryLimitBytes;
        MapLimitBytes = mapLimitBytes;
        DiskLimitBytes = diskLimitBytes;
        ThreadLimit = threadLimit;
        PixelCacheDirectory = Path.GetFullPath(pixelCacheDirectory);
    }

    public ImageResourceCapabilities Resources { get; }
    public ulong MemoryLimitBytes { get; }
    public ulong MapLimitBytes { get; }
    public ulong DiskLimitBytes { get; }
    public int ThreadLimit { get; }
    public string PixelCacheDirectory { get; }

    public static MagickImageProcessorOptions CreateDefault(string pixelCacheDirectory) =>
        new(
            new ImageResourceCapabilities(
                512L * 1024 * 1024,
                32768,
                32768,
                128_000_000,
                32768,
                32768,
                128_000_000),
            512UL * 1024 * 1024,
            1024UL * 1024 * 1024,
            4UL * 1024 * 1024 * 1024,
            Math.Max(1, Math.Min(4, Environment.ProcessorCount)),
            pixelCacheDirectory);
}

internal static class MagickRuntime
{
    private static readonly object SyncRoot = new();

    public static void Configure(MagickImageProcessorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        lock (SyncRoot)
        {
            Directory.CreateDirectory(options.PixelCacheDirectory);

            // Magick.NET 14.14 exposes memory/disk/thread limits directly, while
            // ImageMagick's map limit is configured through its documented process variable.
            Environment.SetEnvironmentVariable(
                "MAGICK_MAP_LIMIT",
                options.MapLimitBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
            MagickNET.SetTempDirectory(options.PixelCacheDirectory);
            ResourceLimits.Memory = options.MemoryLimitBytes;
            ResourceLimits.Disk = options.DiskLimitBytes;
            ResourceLimits.Thread = checked((ulong)options.ThreadLimit);
            ResourceLimits.Width = checked((ulong)options.Resources.MaxInputWidth);
            ResourceLimits.Height = checked((ulong)options.Resources.MaxInputHeight);
        }
    }
}
