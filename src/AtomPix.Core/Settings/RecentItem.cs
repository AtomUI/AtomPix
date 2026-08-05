namespace AtomPix.Core.Settings;

using AtomPix.Core.ValueObjects;

public sealed record RecentItem(LocalPath Path, RecentItemKind Kind, DateTimeOffset OpenedAt);

public enum RecentItemKind
{
    File,
    Directory
}

public static class RecentItemsPolicy
{
    public static IReadOnlyList<RecentItem> AddOrMoveToTop(
        IReadOnlyList<RecentItem> existingItems,
        RecentItem item,
        int maxCount)
    {
        ArgumentNullException.ThrowIfNull(existingItems);
        if (maxCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCount), maxCount, "Recent item count must be greater than zero.");
        }

        var items = existingItems
            .Where(existing => !IsSameRecentTarget(existing, item))
            .Prepend(item)
            .OrderByDescending(existing => existing.OpenedAt)
            .Take(maxCount)
            .ToArray();

        return items;
    }

    public static IReadOnlyList<RecentItem> Normalize(IReadOnlyList<RecentItem> items, int maxCount)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (maxCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCount), maxCount, "Recent item count must be greater than zero.");
        }

        return items
            .GroupBy(item => (NormalizePath(item.Path), item.Kind))
            .Select(group => group.OrderByDescending(item => item.OpenedAt).First())
            .OrderByDescending(item => item.OpenedAt)
            .Take(maxCount)
            .ToArray();
    }

    private static bool IsSameRecentTarget(RecentItem left, RecentItem right) =>
        left.Kind == right.Kind && string.Equals(NormalizePath(left.Path), NormalizePath(right.Path), StringComparison.OrdinalIgnoreCase);

    private static string NormalizePath(LocalPath path) => path.Value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
