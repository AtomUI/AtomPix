namespace AtomPix.Imaging.Magick.Processing;

using ImageMagick;
using AtomPix.Core.ValueObjects;

public enum ImageOutputFailureKind
{
    WriteFailed,
    PermissionDenied,
    InsufficientDiskSpace
}

public sealed class ImageOutputCommitException : IOException
{
    public ImageOutputCommitException(ImageOutputFailureKind kind, Exception innerException)
        : base("Image output could not be committed.", innerException)
    {
        Kind = kind;
    }

    public ImageOutputFailureKind Kind { get; }
}

public interface IImageFileCommitter
{
    void Commit(LocalPath outputPath, Action<string> encodeTemporaryFile);
}

public sealed class AtomicImageFileCommitter : IImageFileCommitter
{
    public void Commit(LocalPath outputPath, Action<string> encodeTemporaryFile)
    {
        ArgumentNullException.ThrowIfNull(encodeTemporaryFile);
        var temporaryPath = CreateTemporaryOutputPath(outputPath);
        try
        {
            PrepareOutputDirectory(outputPath);
            encodeTemporaryFile(temporaryPath);
            if (File.Exists(outputPath.Value))
            {
                File.Replace(temporaryPath, outputPath.Value, null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, outputPath.Value);
            }
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or MagickException
            or ArgumentException
            or NotSupportedException)
        {
            throw new ImageOutputCommitException(Classify(exception), exception);
        }
        finally
        {
            TryDeleteTemporaryOutput(temporaryPath);
        }
    }

    private static ImageOutputFailureKind Classify(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            if (current is UnauthorizedAccessException)
            {
                return ImageOutputFailureKind.PermissionDenied;
            }

            var nativeCode = current.HResult & 0xFFFF;
            if (nativeCode is 28 or 39 or 112)
            {
                return ImageOutputFailureKind.InsufficientDiskSpace;
            }

            if (current.InnerException is null)
            {
                break;
            }
        }

        return ImageOutputFailureKind.WriteFailed;
    }

    private static void PrepareOutputDirectory(LocalPath outputPath)
    {
        var directory = Path.GetDirectoryName(outputPath.Value);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException("Output directory does not exist.");
        }
    }

    private static string CreateTemporaryOutputPath(LocalPath outputPath)
    {
        var directory = Path.GetDirectoryName(outputPath.Value);
        var fileName = Path.GetFileNameWithoutExtension(outputPath.Value);
        var extension = Path.GetExtension(outputPath.Value);
        var safeFileName = string.IsNullOrWhiteSpace(fileName) ? "atompix-output" : fileName;
        var temporaryFileName = $".{safeFileName}.{Guid.NewGuid():N}.tmp{extension}";
        return string.IsNullOrWhiteSpace(directory)
            ? temporaryFileName
            : Path.Combine(directory, temporaryFileName);
    }

    private static void TryDeleteTemporaryOutput(string temporaryPath)
    {
        try
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
