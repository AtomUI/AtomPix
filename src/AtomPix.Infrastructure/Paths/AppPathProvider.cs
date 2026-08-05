namespace AtomPix.Infrastructure.Paths;

using AtomPix.Core.Ports;
using AtomPix.Core.ValueObjects;

public sealed class AppPathProvider : IAppPathProvider
{
    public AppPathProvider() : this(GetDefaultAppDataDirectory(), Path.Combine(Path.GetTempPath(), "AtomPix"))
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
}
