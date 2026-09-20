namespace RomStationRebase.Models;

/// <summary>
/// Une entrée de gamelist.xml prête à écrire : chemins relatifs au dossier système préfixés "./",
/// métadonnées déjà traduites. Les champs null ne sont pas écrits (et ne touchent pas une valeur existante lors d'une fusion).
/// </summary>
public sealed record GamelistGame(
    string  Path,
    string  Name,
    string? Image,
    string? Description,
    int?    Year,
    string? Developer,
    string? Publisher,
    string? Genre,
    string? Players,
    long?   Size = null);

/// <summary>Résultat de l'écriture d'un gamelist.xml.</summary>
public sealed class GamelistWriteResult
{
    /// <summary>True si un fichier existant valide a été fusionné, false s'il a été créé.</summary>
    public bool    Merged     { get; init; }
    /// <summary>Chemin de la sauvegarde d'un fichier existant illisible, null sinon.</summary>
    public string? BackupPath { get; init; }
    /// <summary>Chemin de la copie de sauvegarde demandée par l'utilisateur avant fusion, null sinon.</summary>
    public string? UserBackupPath { get; init; }
    /// <summary>Nombre d'entrées écrites ou mises à jour.</summary>
    public int     Written    { get; init; }
}
