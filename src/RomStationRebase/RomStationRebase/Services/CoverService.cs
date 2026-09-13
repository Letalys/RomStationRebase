using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RomStationRebase.Services;

/// <summary>
/// Copie les jaquettes RomStation vers la destination, avec réduction optionnelle pour les appareils
/// à écran modeste. Fonctionne hors du thread UI : toutes les bitmaps sont gelées.
/// </summary>
public static class CoverService
{
    /// <summary>
    /// Copie une jaquette PNG vers <paramref name="destPath"/> (dossier parent créé).
    /// Si <paramref name="maxWidth"/> ou <paramref name="maxHeight"/> est positif et que l'image dépasse,
    /// elle est réduite en conservant ses proportions (jamais agrandie) et ré-encodée en PNG ;
    /// sinon le fichier est copié tel quel. L'écriture passe par un fichier temporaire puis un remplacement
    /// atomique. Une image source illisible lève : c'est l'appelant qui classifie l'erreur.
    /// </summary>
    /// <returns>Nombre d'octets écrits.</returns>
    public static long CopyCover(string sourcePath, string destPath, int maxWidth, int maxHeight)
    {
        string? parent = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(parent))
            Directory.CreateDirectory(parent);

        if (maxWidth <= 0 && maxHeight <= 0)
            return CopyRaw(sourcePath, destPath);

        BitmapSource source = LoadFrozen(sourcePath);
        double scale = ComputeScale(source.PixelWidth, source.PixelHeight, maxWidth, maxHeight);

        // L'image tient déjà dans la boîte : pas de ré-encodage, on garde les octets d'origine.
        if (scale >= 1.0)
            return CopyRaw(sourcePath, destPath);

        var resized = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        resized.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(resized));

        string tempPath = destPath + ".rsr-tmp";
        try
        {
            using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                encoder.Save(fs);

            File.Move(tempPath, destPath, overwrite: true);
        }
        catch
        {
            try { File.Delete(tempPath); } catch { /* meilleur effort */ }
            throw;
        }

        return new FileInfo(destPath).Length;
    }

    /// <summary>Copie brute avec écrasement, retourne la taille du fichier écrit.</summary>
    private static long CopyRaw(string sourcePath, string destPath)
    {
        File.Copy(sourcePath, destPath, overwrite: true);
        return new FileInfo(destPath).Length;
    }

    /// <summary>
    /// Charge l'image en mémoire depuis un flux (pas d'URI : évite le cache WPF et tout verrou sur le fichier)
    /// et la gèle pour pouvoir l'utiliser depuis n'importe quel thread.
    /// </summary>
    private static BitmapSource LoadFrozen(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption   = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        image.StreamSource  = fs;
        image.EndInit();
        image.Freeze();
        return image;
    }

    /// <summary>
    /// Facteur de réduction pour faire tenir l'image dans la boîte (une dimension à 0 = non contrainte).
    /// Retourne 1.0 ou plus si l'image tient déjà — elle ne sera jamais agrandie.
    /// </summary>
    private static double ComputeScale(int width, int height, int maxWidth, int maxHeight)
    {
        double sx = maxWidth  > 0 && width  > 0 ? (double)maxWidth  / width  : double.PositiveInfinity;
        double sy = maxHeight > 0 && height > 0 ? (double)maxHeight / height : double.PositiveInfinity;
        return Math.Min(sx, sy);
    }
}
