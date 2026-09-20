namespace RomStationRebase.Models;

/// <summary>
/// Formats de fichier de métadonnées qu'une architecture peut demander (champ gamelistFormat).
/// Chaque frontend lit le sien, à un endroit qui lui est propre : ce registre est le seul endroit
/// où se décident le nom et l'emplacement du fichier, et ce qu'il sait porter.
/// </summary>
public static class MetadataFormats
{
    /// <summary>gamelist.xml dans le dossier système : EmulationStation, RetroPie, ArkOS, dArkOS, Batocera, Knulli, ROCKNIX.</summary>
    public const string EmulationStation = "emulationstation";

    /// <summary>gamelist.xml hors des ROMs, dans ES-DE/gamelists/&lt;système&gt;, sans balise image. Aussi le canal d'import de Cocoon.</summary>
    public const string EsDe = "esde";

    /// <summary>miyoogamelist.xml dans le dossier système, réduit à path, name et image : Onion et Spruce (Miyoo).</summary>
    public const string Miyoo = "miyoo";

    /// <summary>metadata.pegasus.txt dans le dossier système : Pegasus Frontend.</summary>
    public const string Pegasus = "pegasus";

    /// <summary>&lt;système&gt;.dat au format Logiqx dans le dossier système : Daijisho, gestionnaires de ROMs.</summary>
    public const string Logiqx = "logiqx";

    /// <summary>Formats connus, dans l'ordre du sélecteur de l'éditeur d'architectures (l'index 0 y est « Aucun »).</summary>
    public static readonly IReadOnlyList<string> All = [EmulationStation, EsDe, Miyoo, Pegasus, Logiqx];

    /// <summary>Identifiant canonique, ou null si le format est absent ou inconnu (fichier d'une version plus récente).</summary>
    public static string? Normalize(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return All.FirstOrDefault(f => string.Equals(f, id.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsKnown(string? id) => Normalize(id) is not null;

    /// <summary>Nom du fichier produit pour un dossier système donné.</summary>
    public static string FileName(string id, string systemFolder) => Normalize(id) switch
    {
        Miyoo   => "miyoogamelist.xml",
        Pegasus => "metadata.pegasus.txt",
        Logiqx  => LeafName(systemFolder) + ".dat",
        _       => "gamelist.xml",
    };

    /// <summary>Chemin du fichier produit, relatif à la destination, séparateur "/".</summary>
    public static string RelativePath(string id, string systemFolder)
    {
        string folder = systemFolder.Replace('\\', '/').Trim('/');
        return Normalize(id) == EsDe
            ? $"ES-DE/gamelists/{folder}/gamelist.xml"
            : $"{folder}/{FileName(id, systemFolder)}";
    }

    /// <summary>Nom à afficher dans l'interface, indépendant du système ("gamelist.xml", "*.dat").</summary>
    public static string DisplayFileName(string? id) => Normalize(id) switch
    {
        null   => string.Empty,
        Logiqx => "*.dat",
        var f  => FileName(f, "system"),
    };

    /// <summary>True si le format désigne la jaquette de chaque jeu. ES-DE la retrouve par son nom, Logiqx n'en connaît pas.</summary>
    public static bool WritesImage(string? id) => Normalize(id) is EmulationStation or Miyoo or Pegasus;

    /// <summary>
    /// True si la jaquette doit reproduire le chemin du fichier lançable, sous-dossier compris :
    /// ES-DE cherche covers/Jeu/Jeu.png pour un jeu rangé dans Jeu/Jeu.cue.
    /// </summary>
    public static bool MirrorsLaunchPath(string? id) => Normalize(id) == EsDe;

    private static string LeafName(string systemFolder)
    {
        string folder = systemFolder.Replace('\\', '/').Trim('/');
        int slash = folder.LastIndexOf('/');
        return slash >= 0 ? folder[(slash + 1)..] : folder;
    }
}
