namespace AtomPix.Infrastructure.Subscriptions;

using System.Text.Json;
using AtomPix.Infrastructure.Storage;
using AtomPix.Core.Errors;
using AtomPix.Core.Licensing;
using AtomPix.Core.Ports;
using AtomPix.Core.Results;

public sealed class LocalSubscriptionStore : ISubscriptionStore
{
    private static readonly JsonSerializerOptions JsonOptions = AtomPixJsonOptions.CreateIndented();
    private readonly IAppPathProvider _pathProvider;

    public LocalSubscriptionStore(IAppPathProvider pathProvider)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
    }

    private string SubscriptionPath => Path.Combine(_pathProvider.AppDataDirectory.Value, "subscription.json");

    public async Task<OperationResult<SubscriptionState>> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(SubscriptionPath))
            {
                return OperationResult<SubscriptionState>.Success(SubscriptionState.Free);
            }

            await using var stream = File.OpenRead(SubscriptionPath);
            var state = await JsonSerializer.DeserializeAsync<SubscriptionState>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            return state is null
                ? InfrastructureErrors.Failure<SubscriptionState>(AtomPixErrorCode.SubscriptionLoadFailed, AtomPixErrorCategory.Configuration, "Subscription file is empty or invalid.")
                : OperationResult<SubscriptionState>.Success(state);
        }
        catch (OperationCanceledException)
        {
            return InfrastructureErrors.Canceled<SubscriptionState>();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return InfrastructureErrors.Failure<SubscriptionState>(AtomPixErrorCode.SubscriptionLoadFailed, AtomPixErrorCategory.Configuration, "Failed to load subscription state.", ex);
        }
    }

    public async Task<OperationResult> SaveAsync(SubscriptionState subscription, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(_pathProvider.AppDataDirectory.Value);
            await JsonFileWriter.WriteAsync(SubscriptionPath, subscription, JsonOptions, cancellationToken).ConfigureAwait(false);
            return OperationResult.Success();
        }
        catch (OperationCanceledException)
        {
            return InfrastructureErrors.Canceled();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return InfrastructureErrors.Failure(AtomPixErrorCode.SubscriptionSaveFailed, AtomPixErrorCategory.Configuration, "Failed to save subscription state.", ex);
        }
    }
}

