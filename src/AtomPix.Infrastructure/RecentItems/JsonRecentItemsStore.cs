namespace AtomPix.Infrastructure.RecentItems;

using System.Text.Json;
using AtomPix.Infrastructure.Storage;
using AtomPix.Core.Errors;
using AtomPix.Core.Ports;
using AtomPix.Core.Results;
using AtomPix.Core.Settings;

public sealed class JsonRecentItemsStore : IRecentItemsStore
{
    private readonly IAppPathProvider _pathProvider;

    public JsonRecentItemsStore(IAppPathProvider pathProvider)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
    }

    private string RecentItemsPath => Path.Combine(_pathProvider.AppDataDirectory.Value, "recent-items.json");

    public async Task<OperationResult<IReadOnlyList<RecentItem>>> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(RecentItemsPath))
            {
                return OperationResult<IReadOnlyList<RecentItem>>.Success(Array.Empty<RecentItem>());
            }

            await using var stream = File.OpenRead(RecentItemsPath);
            var items = await JsonSerializer.DeserializeAsync(
                    stream,
                    AtomPixJsonOptions.Context.ListRecentItem,
                    cancellationToken)
                .ConfigureAwait(false);
            return OperationResult<IReadOnlyList<RecentItem>>.Success(items ?? []);
        }
        catch (OperationCanceledException)
        {
            return InfrastructureErrors.Canceled<IReadOnlyList<RecentItem>>();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return OperationResult<IReadOnlyList<RecentItem>>.Success(Array.Empty<RecentItem>());
        }
    }

    public async Task<OperationResult> SaveAsync(IReadOnlyList<RecentItem> items, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(_pathProvider.AppDataDirectory.Value);
            await JsonFileWriter.WriteAsync(
                    RecentItemsPath,
                    items.ToList(),
                    AtomPixJsonOptions.Context.ListRecentItem,
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
            return InfrastructureErrors.Failure(AtomPixErrorCode.RecentItemsSaveFailed, AtomPixErrorCategory.Configuration, "Failed to save recent items.", ex);
        }
    }
}
