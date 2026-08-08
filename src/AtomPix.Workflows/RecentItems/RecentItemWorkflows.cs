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

public sealed record LoadRecentItemsRequest(int MaxCount);
public sealed record LoadRecentItemsResult(IReadOnlyList<RecentItem> Items);

public sealed class LoadRecentItemsWorkflow
{
    private readonly IRecentItemsStore _recentItemsStore;

    public LoadRecentItemsWorkflow(IRecentItemsStore recentItemsStore) =>
        _recentItemsStore = recentItemsStore ?? throw new ArgumentNullException(nameof(recentItemsStore));

    public async Task<OperationResult<LoadRecentItemsResult>> ExecuteAsync(
        LoadRecentItemsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MaxCount <= 0) throw new ArgumentOutOfRangeException(nameof(request));
        var load = await _recentItemsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        return load.Succeeded
            ? OperationResult<LoadRecentItemsResult>.Success(new LoadRecentItemsResult(RecentItemsPolicy.Normalize(load.Value!, request.MaxCount)))
            : OperationResult<LoadRecentItemsResult>.Failure(load.Error!);
    }
}

public sealed record RemoveRecentItemRequest(LocalPath Path, RecentItemKind Kind);
public sealed record RemoveRecentItemResult(IReadOnlyList<RecentItem> Items);

public sealed class RemoveRecentItemWorkflow
{
    private readonly IRecentItemsStore _recentItemsStore;

    public RemoveRecentItemWorkflow(IRecentItemsStore recentItemsStore) =>
        _recentItemsStore = recentItemsStore ?? throw new ArgumentNullException(nameof(recentItemsStore));

    public async Task<OperationResult<RemoveRecentItemResult>> ExecuteAsync(
        RemoveRecentItemRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var load = await _recentItemsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (!load.Succeeded) return OperationResult<RemoveRecentItemResult>.Failure(load.Error!);

        var updated = load.Value!
            .Where(item => item.Kind != request.Kind || !PathsEqual(item.Path, request.Path))
            .ToArray();
        var save = await _recentItemsStore.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
        return save.Succeeded
            ? OperationResult<RemoveRecentItemResult>.Success(new RemoveRecentItemResult(updated))
            : OperationResult<RemoveRecentItemResult>.Failure(save.Error!);
    }

    private static bool PathsEqual(LocalPath left, LocalPath right) =>
        string.Equals(
            left.Value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            right.Value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
}

public sealed record ClearRecentItemsRequest;
public sealed record ClearRecentItemsResult;

public sealed class ClearRecentItemsWorkflow
{
    private readonly IRecentItemsStore _recentItemsStore;

    public ClearRecentItemsWorkflow(IRecentItemsStore recentItemsStore) =>
        _recentItemsStore = recentItemsStore ?? throw new ArgumentNullException(nameof(recentItemsStore));

    public async Task<OperationResult<ClearRecentItemsResult>> ExecuteAsync(
        ClearRecentItemsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var save = await _recentItemsStore.SaveAsync(Array.Empty<RecentItem>(), cancellationToken).ConfigureAwait(false);
        return save.Succeeded
            ? OperationResult<ClearRecentItemsResult>.Success(new ClearRecentItemsResult())
            : OperationResult<ClearRecentItemsResult>.Failure(save.Error!);
    }
}
