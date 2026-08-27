namespace AtomPix.Infrastructure;

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

internal static class JsonFileWriter
{
    public static async Task WriteAsync<T>(
        string path,
        T value,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken,
        JsonFileCommit? commit = null)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new IOException("JSON file path must include a directory.");
        }

        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 16 * 1024, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, value, typeInfo, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            (commit ?? Commit)(tempPath, path);
        }
        finally
        {
            TryDeleteTemporaryFile(tempPath);
        }
    }

    private static void Commit(string tempPath, string destinationPath) =>
        File.Move(tempPath, destinationPath, overwrite: true);

    private static void TryDeleteTemporaryFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

internal delegate void JsonFileCommit(string tempPath, string destinationPath);

