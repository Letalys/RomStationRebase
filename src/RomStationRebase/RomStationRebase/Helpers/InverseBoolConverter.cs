using System.Globalization;
using System.Windows.Data;

namespace RomStationRebase.Helpers;

/// <summary>Inverse un booléen : true → false, false → true. Utilisé pour les checkboxes à sémantique inversée.</summary>
[ValueConversion(typeof(bool), typeof(bool))]
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;
}
