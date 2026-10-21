namespace AtomPix.Desktop.Localization;

using System.Globalization;
using AtomUI.City.Core.Threading;
using AtomUI.City.Localization;
using AtomUI.Controls;
using AtomUI.Theme.Language;
using Avalonia;

public sealed class AvaloniaPresentationLocalizationBridge(
    IUiDispatcher dispatcher) : IPresentationLocalizationBridge
{
    public async ValueTask<LocalizationResult> ApplyCultureAsync(
        CultureState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        cancellationToken.ThrowIfCancellationRequested();

        await dispatcher.InvokeAsync(
            () => Apply(state.CurrentCulture, state.CurrentUICulture),
            cancellationToken);

        return LocalizationResult.Success();
    }

    private static void Apply(CultureInfo culture, CultureInfo uiCulture)
    {
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = uiCulture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = uiCulture;
        Application.Current?.SetLanguageVariant(
            uiCulture.Name.Equals("zh-CN", StringComparison.OrdinalIgnoreCase)
                ? LanguageVariant.zh_CN
                : LanguageVariant.en_US);
    }
}
