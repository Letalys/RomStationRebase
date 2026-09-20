namespace RomStationRebase.Models;

/// <summary>Table de correspondance système RomStation → dossier cible, désérialisée depuis un fichier de mapping JSON.</summary>
public class FolderTreeMapping
{
    public List<SystemMapping> FolderTreeMappings { get; set; } = [];
}

/// <summary>
/// Association entre un nom de système RomStation et le nom du dossier cible (ex : "Playstation" → "psx"),
/// avec les règles de sortie propres à ce système sur cette cible.
/// </summary>
public class SystemMapping
{
    public string RomStationSystem { get; set; } = string.Empty;
    public string TargetFolder     { get; set; } = string.Empty;

    /// <summary>
    /// Conserver le nom d'origine des fichiers (romsets arcade : FBNeo et MAME identifient le jeu par le nom de l'archive).
    /// Un système marqué ainsi n'est jamais extrait et ne reçoit jamais de M3U.
    /// </summary>
    public bool KeepFileName { get; set; }

    /// <summary>L'émulateur de ce système lit les playlists M3U sur cette cible.</summary>
    public bool M3U { get; set; }

    /// <summary>L'émulateur de ce système ne lit pas les archives : extraction requise sur cette cible.</summary>
    public bool Extract { get; set; }

    /// <summary>
    /// Identifiant de l'outil externe qui convertit les fichiers de ce système pour cette cible (ex : "chdman_createcd_default"
    /// pour la Dreamcast, stockée en GDI par RomStation et attendue en CHD). Null ou vide : aucune conversion.
    /// </summary>
    public string? Transform { get; set; }
}
