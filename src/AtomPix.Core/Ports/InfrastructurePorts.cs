namespace AtomPix.Core.Ports;

using AtomPix.Core.Licensing;
using AtomPix.Core.Output;
using AtomPix.Core.Results;
using AtomPix.Core.Settings;
using AtomPix.Core.ValueObjects;

public interface IAppSettingsStore
{
    Task<OperationResult<AppSettings>> LoadAsync(CancellationToken cancellationToken);

    Task<OperationResult> SaveAsync(AppSettings settings, CancellationToken cancellationToken);
}

public interface ISubscriptionStore
{
    Task<OperationResult<SubscriptionState>> LoadAsync(CancellationToken cancellationToken);

    Task<OperationResult> SaveAsync(SubscriptionState subscription, CancellationToken cancellationToken);
}

public interface IRecentItemsStore
{
    Task<OperationResult<IReadOnlyList<RecentItem>>> LoadAsync(CancellationToken cancellationToken);

    Task<OperationResult> SaveAsync(IReadOnlyList<RecentItem> items, CancellationToken cancellationToken);
}

public interface IFileSystemService
{
    bool FileExists(LocalPath path);

    bool DirectoryExists(LocalPath path);

    Task<OperationResult> CreateDirectoryAsync(LocalPath directory, CancellationToken cancellationToken);

    Task<OperationResult<long>> GetFileSizeAsync(LocalPath path, CancellationToken cancellationToken);

    LocalPath Combine(LocalPath directory, string fileName);

    string GetFileNameWithoutExtension(LocalPath path);

    string GetExtension(LocalPath path);

    LocalPath ChangeExtension(LocalPath path, string extension);

    LocalPath BuildIndexedPath(LocalPath basePath, int index);
}

public interface IAppPathProvider
{
    LocalPath AppDataDirectory { get; }

    LocalPath TempDirectory { get; }
}