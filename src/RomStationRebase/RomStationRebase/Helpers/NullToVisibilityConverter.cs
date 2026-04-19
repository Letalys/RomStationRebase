using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RomStationRebase.Helpers;

/// <summary>
/// Convertit une valeur en Visibility : null ou chaîne vide → Collapsed, toute autre valeur → Visible.
/// Utilisé pour afficher le placeholder de jaquette quand CoverPath est vide.
/// Paramètre optionnel "Invert" pour inverser le comportement (null → Visible).
/// </summary>
[ValueConversion(typeof(object), typeof(Visibility))]
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isEmpty = value is null || (value is string s && s.Length == 0);
        bool invert  = parameter is string p && p == "Invert";

        return (isEmpty ^ invert) ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
