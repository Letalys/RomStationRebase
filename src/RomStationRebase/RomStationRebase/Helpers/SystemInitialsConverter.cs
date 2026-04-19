using System.Globalization;
using System.Windows.Data;

namespace RomStationRebase.Helpers;

/// <summary>
/// Convertit un nom de système en sigle court pour le placeholder de jaquette.
/// Stratégie : première lettre de chaque mot (max 4) ;
/// si un seul mot, prend les 3 premiers caractères en majuscules.
/// Ex : "Super Nintendo" → "SN", "Nintendo 64" → "N64", "PlayStation" → "PLA".
/// </summary>
[ValueConversion(typeof(string), typeof(string))]
public class SystemInitialsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string name || string.IsNullOrWhiteSpace(name))
            return "?";

        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (words.Length == 1)
            return name.Length >= 3 ? name[..3].ToUpperInvariant() : name.ToUpperInvariant();

        // Pour chaque mot : première lettre si alphabétique, sinon le token lui-même (ex: "64")
        var parts = words.Select(w => char.IsLetter(w[0])
            ? char.ToUpperInvariant(w[0]).ToString()
            : w);

        var result = string.Concat(parts);
        return result.Length > 4 ? result[..4] : result;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
