namespace AtomPix.Core.Settings;

using AtomPix.Core.Compression;
using AtomPix.Core.Conversion;
using AtomPix.Core.Output;

public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public AppSettings(
        CompressionProfile defaultCompressionProfile,
        ConversionProfile defaultConversionProfile,
        OutputPolicy defaultOutputPolicy,
        ThemeMode themeMode,
        string? language,
        RecentItemsSettings recentItems,
        int schemaVersion = CurrentSchemaVersion)
    {
        if (schemaVersion <= 0 || schemaVersion > CurrentSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaVersion), schemaVersion, "Unsupported settings schema version.");
        }

        SchemaVersion = schemaVersion;
        DefaultCompressionProfile = defaultCompressionProfile ?? throw new ArgumentNullException(nameof(defaultCompressionProfile));
        DefaultConversionProfile = defaultConversionProfile ?? throw new ArgumentNullException(nameof(defaultConversionProfile));
        DefaultOutputPolicy = defaultOutputPolicy ?? throw new ArgumentNullException(nameof(defaultOutputPolicy));
        ThemeMode = themeMode;
        Language = string.IsNullOrWhiteSpace(language) ? null : language;
        RecentItems = recentItems ?? throw new ArgumentNullException(nameof(recentItems));
    }

    public int SchemaVersion { get; }

    public CompressionProfile DefaultCompressionProfile { get; }

    public ConversionProfile DefaultConversionProfile { get; }

    public OutputPolicy DefaultOutputPolicy { get; }

    public ThemeMode ThemeMode { get; }

    public string? Language { get; }

    public RecentItemsSettings RecentItems { get; }

    public static AppSettings Default { get; } = new(
        CompressionProfile.SmartDefault(),
        ConversionProfile.WebPDefault(),
        OutputPolicy.Default,
        ThemeMode.System,
        null,
        new RecentItemsSettings(true, 20));
}

public enum ThemeMode
{
    System,
    Light,
    Dark
}

public sealed record RecentItemsSettings
{
    public RecentItemsSettings(bool enabled, int maxCount)
    {
        if (maxCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCount), maxCount, "Recent item count must be greater than zero.");
        }

        Enabled = enabled;
        MaxCount = maxCount;
    }

    public bool Enabled { get; }

    public int MaxCount { get; }
}
