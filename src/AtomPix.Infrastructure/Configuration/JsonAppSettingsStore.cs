namespace AtomPix.Infrastructure.Configuration;

using System.Text.Json;
using AtomPix.Infrastructure.Storage;
using AtomPix.Core.Errors;
using AtomPix.Core.Compression;
using AtomPix.Core.Conversion;
using AtomPix.Core.Output;
using AtomPix.Core.Ports;
using AtomPix.Core.Resize;
using AtomPix.Core.Results;
using AtomPix.Core.Settings;

public sealed class JsonAppSettingsStore : IAppSettingsStore
{
    private readonly IAppPathProvider _pathProvider;

    public JsonAppSettingsStore(IAppPathProvider pathProvider)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
    }

    private string SettingsPath => Path.Combine(_pathProvider.AppDataDirectory.Value, "settings.json");

    public async Task<OperationResult<AppSettings>> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(SettingsPath))
            {
                return OperationResult<AppSettings>.Success(AppSettings.Default);
            }

            await using var stream = File.OpenRead(SettingsPath);
            var persisted = await JsonSerializer.DeserializeAsync(
                    stream,
                    AtomPixJsonOptions.Context.PersistedAppSettings,
                    cancellationToken)
                .ConfigureAwait(false);
            if (persisted is null)
            {
                return InfrastructureErrors.Failure<AppSettings>(AtomPixErrorCode.SettingsLoadFailed, AtomPixErrorCategory.Configuration, "Settings file is empty or invalid.");
            }

            var metadataPolicy = persisted.DefaultCompressionProfile?.MetadataPolicy ?? MetadataPolicy.Remove;
            var sameFormatPolicy = persisted.DefaultSameFormatEncodingPolicy
                ?? new SameFormatEncodingPolicy(SameFormatEncodingPolicy.Default.LossyQuality, metadataPolicy);
            var settings = new AppSettings(
                persisted.DefaultCompressionProfile!,
                persisted.DefaultConversionProfile!,
                sameFormatPolicy,
                persisted.DefaultOutputPolicy!,
                persisted.ThemeMode ?? ThemeMode.System,
                persisted.Language,
                persisted.RecentItems!,
                persisted.SchemaVersion ?? AppSettings.CurrentSchemaVersion);
            return OperationResult<AppSettings>.Success(settings);
        }
        catch (OperationCanceledException)
        {
            return InfrastructureErrors.Canceled<AppSettings>();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return InfrastructureErrors.Failure<AppSettings>(AtomPixErrorCode.SettingsLoadFailed, AtomPixErrorCategory.Configuration, "Failed to load settings.", ex);
        }
    }

    public async Task<OperationResult> SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(_pathProvider.AppDataDirectory.Value);
            var persisted = PersistedAppSettings.From(settings);
            await JsonFileWriter.WriteAsync(
                    SettingsPath,
                    persisted,
                    AtomPixJsonOptions.Context.PersistedAppSettings,
                    cancellationToken)
                .ConfigureAwait(false);
            return OperationResult.Success();
        }
        catch (OperationCanceledException)
        {
            return InfrastructureErrors.Canceled();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return InfrastructureErrors.Failure(AtomPixErrorCode.SettingsSaveFailed, AtomPixErrorCategory.Configuration, "Failed to save settings.", ex);
        }
    }

}

internal sealed class PersistedAppSettings
{
    public int? SchemaVersion { get; init; }

    public CompressionProfile? DefaultCompressionProfile { get; init; }

    public ConversionProfile? DefaultConversionProfile { get; init; }

    public SameFormatEncodingPolicy? DefaultSameFormatEncodingPolicy { get; init; }

    public OutputPolicy? DefaultOutputPolicy { get; init; }

    public ThemeMode? ThemeMode { get; init; }

    public string? Language { get; init; }

    public RecentItemsSettings? RecentItems { get; init; }

    public static PersistedAppSettings From(AppSettings settings) => new()
    {
        SchemaVersion = settings.SchemaVersion,
        DefaultCompressionProfile = settings.DefaultCompressionProfile,
        DefaultConversionProfile = settings.DefaultConversionProfile,
        DefaultSameFormatEncodingPolicy = settings.DefaultSameFormatEncodingPolicy,
        DefaultOutputPolicy = settings.DefaultOutputPolicy,
        ThemeMode = settings.ThemeMode,
        Language = settings.Language,
        RecentItems = settings.RecentItems
    };
}

