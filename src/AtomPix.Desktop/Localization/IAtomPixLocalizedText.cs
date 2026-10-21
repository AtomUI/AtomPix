namespace AtomPix.Desktop.Localization;

using System.ComponentModel;
using System.Globalization;

public interface IAtomPixLocalizedText : INotifyPropertyChanged
{
    event EventHandler? LanguageChanged;

    CultureInfo CurrentCulture { get; }

    long Revision { get; }

    string GetString(string key);

    string Format(string key, params object[] arguments);
}
