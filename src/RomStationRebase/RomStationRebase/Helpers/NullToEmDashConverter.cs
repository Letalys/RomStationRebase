using System.Globalization;
using System.Windows.Data;

namespace RomStationRebase.Helpers;

/// <summary>Convertit null ou chaîne vide en "—" ; toute autre valeur est renvoyée telle quelle.</summary>
[ValueConversion(typeof(object), typeof(object))]
public class NullToEmDashConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object parameter, CultureInfo culture)
        => value is null || (value is string s && s.Length == 0) ? "—" : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
