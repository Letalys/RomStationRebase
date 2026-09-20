using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RomStationRebase.Helpers;

/// <summary>
/// Convertit un chemin de fichier (string) en ImageSource.
/// Retourne null si la chaîne est vide ou null, évitant l'erreur WPF
/// "Cannot convert null to ImageSource" quand CoverPath n'est pas renseigné.
/// Le paramètre optionnel (entier) fixe la largeur de décodage : une jaquette de 600×800 décodée à 400 px
/// coûte quatre fois moins de mémoire et de temps qu'en pleine taille — c'est ce qui fluidifie la mosaïque.
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

            int decodeWidth = parameter switch
            {
                int i    => i,
                string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) => parsed,
                _        => 0,
            };

            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource   = uri;
            image.CacheOption = BitmapCacheOption.OnLoad; // fichier lu d'un coup, handle libéré aussitôt
            if (decodeWidth > 0)
                image.DecodePixelWidth = decodeWidth;
            image.EndInit();
            image.Freeze(); // partageable entre threads, plus léger pour le rendu
            return image;
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
