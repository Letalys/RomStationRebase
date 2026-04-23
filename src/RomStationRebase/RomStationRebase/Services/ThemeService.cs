using System;
using System.Linq;
using System.Windows;

namespace RomStationRebase.Services;

/// <summary>Gestion du thème visuel (Light / Dark) — swap à chaud des tokens couleur.</summary>
public static class ThemeService
{
    private static readonly string[] Supported = { "Light", "Dark" };

    public static string Current { get; private set; } = "Light";

    /// <summary>
    /// Applique le thème demandé en remplaçant le dictionnaire ColorTokens dans MergedDictionaries.
    /// Si le thème est déjà actif, retour immédiat sans modification.
    /// </summary>
    public static void Apply(string theme)
    {
        if (!Supported.Contains(theme))
            theme = "Light";

        if (string.Equals(Current, theme, StringComparison.Ordinal))
            return;

        var dicts = Application.Current.Resources.MergedDictionaries;

        var old = dicts.FirstOrDefault(d =>
            d.Source != null &&
            d.Source.OriginalString.Contains("ColorTokens.", StringComparison.OrdinalIgnoreCase));

        if (old != null)
            dicts.Remove(old);

        var uri     = new Uri($"Resources/Styles/ColorTokens.{theme}.xaml", UriKind.Relative);
        var newDict = new ResourceDictionary { Source = uri };
        dicts.Insert(0, newDict);

        Current = theme;
    }
}
