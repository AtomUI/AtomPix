namespace AtomPix.Infrastructure.Configuration;

using System.Text.Json;
using AtomPix.Infrastructure.Storage;
using AtomPix.Core.Errors;
using AtomPix.Core.Ports;
using AtomPix.Core.Results;
using AtomPix.Core.Settings;

public sealed class JsonAppSettingsStore : IAppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = AtomPixJsonOptions.CreateIndented();
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
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            return settings is null
                ? InfrastructureErrors.Failure<AppSettings>(AtomPixErrorCode.SettingsLoadFailed, AtomPixErrorCategory.Configuration, "Settings file is empty or invalid.")
                : OperationResult<AppSettings>.Success(settings);
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
            await JsonFileWriter.WriteAsync(SettingsPath, settings, JsonOptions, cancellationToken).ConfigureAwait(false);
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

