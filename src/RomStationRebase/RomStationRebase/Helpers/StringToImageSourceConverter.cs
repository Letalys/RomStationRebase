using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RomStationRebase.Helpers;

/// <summary>
/// Convertit un chemin de fichier (string) en ImageSource.
/// Retourne null si la chaîne est vide ou null, évitant l'erreur WPF
/// "Cannot convert null to ImageSource" quand CoverPath n'est pas renseigné.
/// </summary>
[ValueConversion(typeof(string), typeof(ImageSource))]
public class StringToImageSourceConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrEmpty(path))
            return null;

        try
        {
            Uri uri;
            if (Uri.TryCreate(path, UriKind.Absolute, out var absUri))
            {
                // URI absolue valide : pack://, file://, http://…
                uri = absUri;
            }
            else if (System.IO.Path.IsPathRooted(path))
            {
                // Chemin disque absolu Windows : C:\, J:\, \\serveur\…
                uri = new Uri(path);
            }
            else
            {
                // Chemin relatif WPF : /Resources/…
                uri = new Uri(path, UriKind.Relative);
            }

            return new BitmapImage(uri);
        }
        catch
        {
            // Fichier introuvable ou URI invalide — affiche le placeholder
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
