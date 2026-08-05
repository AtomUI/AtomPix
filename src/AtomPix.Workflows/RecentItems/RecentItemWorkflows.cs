namespace AtomPix.Workflows.RecentItems;

using AtomPix.Core.Ports;
using AtomPix.Core.Results;
using AtomPix.Core.Settings;
using AtomPix.Core.ValueObjects;

public sealed record AddRecentItemRequest(LocalPath Path, RecentItemKind Kind, DateTimeOffset OpenedAt, int MaxCount);
public sealed record AddRecentItemResult(IReadOnlyList<RecentItem> Items);

public sealed class AddRecentItemWorkflow
{
    private readonly IRecentItemsStore _recentItemsStore;

    public AddRecentItemWorkflow(IRecentItemsStore recentItemsStore)
    {
        _recentItemsStore = recentItemsStore ?? throw new ArgumentNullException(nameof(recentItemsStore));
    }

    public async Task<OperationResult<AddRecentItemResult>> ExecuteAsync(AddRecentItemRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MaxCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.MaxCount, "Max count must be greater than zero.");
        }

        var load = await _recentItemsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (!load.Succeeded)
        {
            return OperationResult<AddRecentItemResult>.Failure(load.Error!);
        }

        var updated = RecentItemsPolicy.AddOrMoveToTop(
            load.Value!,
            new RecentItem(request.Path, request.Kind, request.OpenedAt),
            request.MaxCount);

        var save = await _recentItemsStore.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
        return save.Succeeded
            ? OperationResult<AddRecentItemResult>.Success(new AddRecentItemResult(updated))
            : OperationResult<AddRecentItemResult>.Failure(save.Error!);
    }
}
