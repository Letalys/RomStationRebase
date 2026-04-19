using System.Globalization;
using System.Windows.Data;
using RomStationRebase.Resources;

namespace RomStationRebase.Helpers;

/// <summary>Convertit un entier FileCount en chaîne localisée : "1 fichier" ou "N fichiers".</summary>
[ValueConversion(typeof(int), typeof(string))]
public class FileCountConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int count)
            return count == 1 ? Strings.Badge_SingleFile : string.Format(Strings.Badge_MultiDisc, count);
        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
