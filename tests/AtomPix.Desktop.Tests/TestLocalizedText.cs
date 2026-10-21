namespace AtomPix.Desktop.Tests;

using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using AtomPix.Desktop.Localization;

internal sealed class TestLocalizedText : IAtomPixLocalizedText
{
    private static readonly string[] Catalogs =
    [
        "Batch",
        "Browser",
        "Common",
        "Errors",
        "Home",
        "Settings",
        "Shell",
        "Tools",
    ];

    private readonly IReadOnlyDictionary<string, string> _resources = LoadResources();

    private TestLocalizedText()
    {
    }

    public static TestLocalizedText Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged
    {
        add { }
        remove { }
    }

    public event EventHandler? LanguageChanged
    {
        add { }
        remove { }
    }

    public CultureInfo CurrentCulture { get; } = CultureInfo.GetCultureInfo("zh-CN");

    public long Revision => 0;

    public string GetString(string key) =>
        _resources.TryGetValue(key, out var value) ? value : $"[[{key}]]";

    public string Format(string key, params object[] arguments) =>
        string.Format(CurrentCulture, GetString(key), arguments);

    private static IReadOnlyDictionary<string, string> LoadResources()
    {
        var assembly = typeof(IAtomPixLocalizedText).Assembly;
        var resources = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var catalog in Catalogs)
        {
            var resourceName = $"AtomPix.Desktop.Localization.{catalog}.zh-CN.Resources.locpack.json";
            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded localization catalog is missing: {resourceName}");
            using var document = JsonDocument.Parse(stream);
            foreach (var property in document.RootElement.GetProperty("resources").EnumerateObject())
            {
                resources.Add(property.Name, property.Value.GetString() ?? string.Empty);
            }
        }

        return resources;
    }
}
