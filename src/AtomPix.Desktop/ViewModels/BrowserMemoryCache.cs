namespace AtomPix.Desktop.ViewModels;

public sealed record BrowserCacheOptions
{
    public BrowserCacheOptions(
        long previewByteBudget = 128L * 1024 * 1024,
        int previewEntryLimit = 64,
        long thumbnailByteBudget = 64L * 1024 * 1024,
        int thumbnailEntryLimit = 512,
        int probeEntryLimit = 4096)
    {
        if (previewByteBudget <= 0) throw new ArgumentOutOfRangeException(nameof(previewByteBudget));
        if (previewEntryLimit <= 0) throw new ArgumentOutOfRangeException(nameof(previewEntryLimit));
        if (thumbnailByteBudget <= 0) throw new ArgumentOutOfRangeException(nameof(thumbnailByteBudget));
        if (thumbnailEntryLimit <= 0) throw new ArgumentOutOfRangeException(nameof(thumbnailEntryLimit));
        if (probeEntryLimit <= 0) throw new ArgumentOutOfRangeException(nameof(probeEntryLimit));

        PreviewByteBudget = previewByteBudget;
        PreviewEntryLimit = previewEntryLimit;
        ThumbnailByteBudget = thumbnailByteBudget;
        ThumbnailEntryLimit = thumbnailEntryLimit;
        ProbeEntryLimit = probeEntryLimit;
    }

    public long PreviewByteBudget { get; }

    public int PreviewEntryLimit { get; }

    public long ThumbnailByteBudget { get; }

    public int ThumbnailEntryLimit { get; }

    public int ProbeEntryLimit { get; }
}

public sealed record BrowserCacheSnapshot(
    int PreviewEntryCount,
    long PreviewBytes,
    int RetainedThumbnailCount,
    long RetainedThumbnailBytes,
    int ProbeEntryCount);

internal sealed class BoundedLruCache<TKey, TValue>
    where TKey : notnull
{
    private readonly int _entryLimit;
    private readonly long _sizeLimit;
    private readonly Func<TValue, long> _sizeSelector;
    private readonly Dictionary<TKey, LinkedListNode<Entry>> _entries;
    private readonly LinkedList<Entry> _recency = new();

    public BoundedLruCache(
        int entryLimit,
        long sizeLimit,
        Func<TValue, long> sizeSelector,
        IEqualityComparer<TKey>? comparer = null)
    {
        if (entryLimit <= 0) throw new ArgumentOutOfRangeException(nameof(entryLimit));
        if (sizeLimit <= 0) throw new ArgumentOutOfRangeException(nameof(sizeLimit));
        _entryLimit = entryLimit;
        _sizeLimit = sizeLimit;
        _sizeSelector = sizeSelector ?? throw new ArgumentNullException(nameof(sizeSelector));
        _entries = new Dictionary<TKey, LinkedListNode<Entry>>(comparer);
    }

    public int Count => _entries.Count;

    public long TotalSize { get; private set; }

    public bool TryGetValue(TKey key, out TValue? value)
    {
        if (!_entries.TryGetValue(key, out var node))
        {
            value = default;
            return false;
        }

        _recency.Remove(node);
        _recency.AddFirst(node);
        value = node.Value.Value;
        return true;
    }

    public IReadOnlyList<TValue> Set(TKey key, TValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (_entries.TryGetValue(key, out var existing))
        {
            RemoveNode(existing);
        }

        var size = Math.Max(0, _sizeSelector(value));
        var node = new LinkedListNode<Entry>(new Entry(key, value, size));
        _recency.AddFirst(node);
        _entries.Add(key, node);
        TotalSize = checked(TotalSize + size);

        List<TValue>? evicted = null;
        while (_entries.Count > _entryLimit || TotalSize > _sizeLimit)
        {
            var oldest = _recency.Last!;
            (evicted ??= []).Add(oldest.Value.Value);
            RemoveNode(oldest);
        }

        return evicted is null ? Array.Empty<TValue>() : evicted;
    }

    public void Clear()
    {
        _entries.Clear();
        _recency.Clear();
        TotalSize = 0;
    }

    private void RemoveNode(LinkedListNode<Entry> node)
    {
        _recency.Remove(node);
        _entries.Remove(node.Value.Key);
        TotalSize -= node.Value.Size;
    }

    private sealed record Entry(TKey Key, TValue Value, long Size);
}
