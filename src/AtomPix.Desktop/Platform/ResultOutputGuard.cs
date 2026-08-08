namespace AtomPix.Desktop.Platform;

using AtomPix.Core.Ports;
using AtomPix.Core.ValueObjects;

public sealed class ResultOutputGuard
{
    private readonly IFileSystemService _fileSystem;

    public ResultOutputGuard(IFileSystemService fileSystem) =>
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    public bool FileExists(LocalPath? path) => path is { } value && _fileSystem.FileExists(value);

    public bool DirectoryExists(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            return _fileSystem.DirectoryExists(new LocalPath(path));
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
