namespace AtomPix.Desktop.Localization;

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;

public sealed class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string resource)
    {
        Resource = resource;
    }

    public string Resource { get; set; } = string.Empty;

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:RequiresUnreferencedCode",
        Justification = "The reflection binding targets the public IAtomPixLocalizedText.Revision contract, which is statically preserved and covered by trimmed published UI automation.")]
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Resource);
        var localization = LocalizationHost.Current
            ?? throw new InvalidOperationException("AtomPix localization has not been initialized.");

        return new Binding(nameof(IAtomPixLocalizedText.Revision))
        {
            Source = localization,
            Mode = BindingMode.OneWay,
            Converter = new ResourceConverter(localization, Resource),
        };
    }

    private sealed class ResourceConverter(
        IAtomPixLocalizedText localization,
        string key) : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            localization.GetString(key);

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
