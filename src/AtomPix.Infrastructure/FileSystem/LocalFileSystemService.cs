namespace AtomPix.Infrastructure.FileSystem;

using AtomPix.Core.Errors;
using AtomPix.Core.Ports;
using AtomPix.Core.Results;
using AtomPix.Core.ValueObjects;

public sealed class LocalFileSystemService : IFileSystemService
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public bool FileExists(LocalPath path) => File.Exists(path.Value);

    public bool DirectoryExists(LocalPath path) => Directory.Exists(path.Value);

    public Task<OperationResult> CreateDirectoryAsync(LocalPath directory, CancellationToken cancellationToken)
    {
        return RunOnWorkerAsync(() => CreateDirectoryOnWorkerAsync(directory, cancellationToken));
    }

    private static Task<OperationResult> CreateDirectoryOnWorkerAsync(LocalPath directory, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(directory.Value);
            return Task.FromResult(OperationResult.Success());
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(InfrastructureErrors.Canceled());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Task.FromResult(InfrastructureErrors.Failure(AtomPixErrorCode.OutputDirectoryNotFound, AtomPixErrorCategory.FileSystem, "Failed to create directory.", ex));
        }
    }

    public Task<OperationResult<long>> GetFileSizeAsync(LocalPath path, CancellationToken cancellationToken)
    {
        return RunOnWorkerAsync(() => GetFileSizeOnWorkerAsync(path, cancellationToken));
    }

    private static Task<OperationResult<long>> GetFileSizeOnWorkerAsync(LocalPath path, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(path.Value);
            if (!info.Exists)
            {
                return Task.FromResult(InfrastructureErrors.Failure<long>(AtomPixErrorCode.InputFileNotFound, AtomPixErrorCategory.FileSystem, "File does not exist."));
            }

            return Task.FromResult(OperationResult<long>.Success(info.Length));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(InfrastructureErrors.Canceled<long>());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Task.FromResult(InfrastructureErrors.Failure<long>(AtomPixErrorCode.InputFileNotFound, AtomPixErrorCategory.FileSystem, "Failed to get file size.", ex));
        }
    }

    public Task<OperationResult<IReadOnlyList<LocalPath>>> EnumerateFilesAsync(
        LocalPath directory,
        CancellationToken cancellationToken)
    {
        return RunOnWorkerAsync(() => EnumerateFilesOnWorkerAsync(directory, cancellationToken));
    }

    private static Task<OperationResult<IReadOnlyList<LocalPath>>> EnumerateFilesOnWorkerAsync(
        LocalPath directory,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(directory.Value))
            {
                return Task.FromResult(InfrastructureErrors.Failure<IReadOnlyList<LocalPath>>(
                    AtomPixErrorCode.InputDirectoryNotFound,
                    AtomPixErrorCategory.FileSystem,
                    "Input directory does not exist."));
            }

            var files = Directory
                .EnumerateFiles(directory.Value, "*", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFullPath)
                .Select(path => new LocalPath(path))
                .ToArray();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(OperationResult<IReadOnlyList<LocalPath>>.Success(files));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(InfrastructureErrors.Canceled<IReadOnlyList<LocalPath>>());
        }
        catch (UnauthorizedAccessException ex)
        {
            return Task.FromResult(InfrastructureErrors.Failure<IReadOnlyList<LocalPath>>(
                AtomPixErrorCode.Unknown,
                AtomPixErrorCategory.Permission,
                "Access to the input directory was denied.",
                ex));
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
        {
            return Task.FromResult(InfrastructureErrors.Failure<IReadOnlyList<LocalPath>>(
                AtomPixErrorCode.Unknown,
                AtomPixErrorCategory.FileSystem,
                "Failed to enumerate input directory.",
                ex));
        }
    }

    private static Task<T> RunOnWorkerAsync<T>(Func<Task<T>> operation) =>
        Task.Run(operation, CancellationToken.None);

    public OperationResult<LocalPath> NormalizePath(LocalPath path)
    {
        try
        {
            return OperationResult<LocalPath>.Success(new LocalPath(Path.GetFullPath(path.Value)));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return InfrastructureErrors.Failure<LocalPath>(
                AtomPixErrorCode.InvalidInputPath,
                AtomPixErrorCategory.Validation,
                "Path cannot be normalized.",
                ex);
        }
    }

    public bool PathsEqual(LocalPath left, LocalPath right)
    {
        return PathComparer.Equals(Path.GetFullPath(left.Value), Path.GetFullPath(right.Value));
    }

    public int ComparePaths(LocalPath left, LocalPath right)
    {
        return PathComparer.Compare(Path.GetFullPath(left.Value), Path.GetFullPath(right.Value));
    }

    public LocalPath Combine(LocalPath directory, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("File name cannot be empty.", nameof(fileName));
        }

        if (fileName is "." or ".." || Path.IsPathRooted(fileName) || fileName.Contains('/') || fileName.Contains('\\'))
        {
            throw new ArgumentException("File name must be a single path segment.", nameof(fileName));
        }

        return new LocalPath(Path.Combine(directory.Value, fileName));
    }

    public string GetFileName(LocalPath path) => Path.GetFileName(path.Value);

    public string GetFileNameWithoutExtension(LocalPath path) => Path.GetFileNameWithoutExtension(path.Value);

    public string GetExtension(LocalPath path) => Path.GetExtension(path.Value);

    public LocalPath ChangeExtension(LocalPath path, string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            throw new ArgumentException("Extension cannot be empty.", nameof(extension));
        }

        var normalized = extension.StartsWith(".", StringComparison.Ordinal) ? extension : "." + extension;
        return new LocalPath(Path.ChangeExtension(path.Value, normalized) ?? throw new ArgumentException("Path cannot be changed to the requested extension.", nameof(path)));
    }

    public LocalPath BuildIndexedPath(LocalPath basePath, int index)
    {
        if (index <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "Index must be positive.");
        }

        var directory = Path.GetDirectoryName(basePath.Value) ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(basePath.Value);
        var extension = Path.GetExtension(basePath.Value);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("Base path must include a file name.", nameof(basePath));
        }

        return new LocalPath(Path.Combine(directory, $"{fileName}_{index}{extension}"));
    }
}

