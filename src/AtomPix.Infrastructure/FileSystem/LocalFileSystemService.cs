namespace AtomPix.Infrastructure.FileSystem;

using AtomPix.Core.Errors;
using AtomPix.Core.Ports;
using AtomPix.Core.Results;
using AtomPix.Core.ValueObjects;

public sealed class LocalFileSystemService : IFileSystemService
{
    public bool FileExists(LocalPath path) => File.Exists(path.Value);

    public bool DirectoryExists(LocalPath path) => Directory.Exists(path.Value);

    public Task<OperationResult> CreateDirectoryAsync(LocalPath directory, CancellationToken cancellationToken)
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

