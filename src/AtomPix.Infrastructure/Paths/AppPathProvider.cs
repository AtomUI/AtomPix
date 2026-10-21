namespace AtomPix.Infrastructure.Paths;

using AtomPix.Core.Ports;
using AtomPix.Core.ValueObjects;

public sealed class AppPathProvider : IAppPathProvider
{
    public const string AppDataDirectoryEnvironmentVariable = "ATOMPIX_APPDATA_DIRECTORY";
    public const string TempDirectoryEnvironmentVariable = "ATOMPIX_TEMP_DIRECTORY";

    public AppPathProvider() : this(GetDefaultAppDataDirectory(), GetDefaultTempDirectory())
    {
    }

    public AppPathProvider(string appDataDirectory, string tempDirectory)
    {
        AppDataDirectory = new LocalPath(appDataDirectory);
        TempDirectory = new LocalPath(tempDirectory);
    }

    public LocalPath AppDataDirectory { get; }

    public LocalPath TempDirectory { get; }

    private static string GetDefaultAppDataDirectory()
    {
        var configured = GetConfiguredDirectory(AppDataDirectoryEnvironmentVariable);
        if (configured is not null)
        {
            return configured;
        }

        if (OperatingSystem.IsWindows())
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "AtomPix");
        }

        if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Application Support", "AtomPix");
        }

        var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(xdgDataHome))
        {
            return Path.Combine(xdgDataHome, "AtomPix");
        }

        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userHome, ".local", "share", "AtomPix");
    }

    private static string GetDefaultTempDirectory() =>
        GetConfiguredDirectory(TempDirectoryEnvironmentVariable)
        ?? Path.Combine(Path.GetTempPath(), "AtomPix");

    private static string? GetConfiguredDirectory(string variableName)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!Path.IsPathFullyQualified(value))
        {
            throw new InvalidOperationException(
                $"Environment variable '{variableName}' must contain an absolute path.");
        }

        return Path.GetFullPath(value);
    }
}
