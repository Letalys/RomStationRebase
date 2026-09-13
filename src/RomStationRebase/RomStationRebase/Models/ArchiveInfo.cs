namespace RomStationRebase.Models;

/// <summary>
/// Résultat de l'inspection d'une archive source (lecture du répertoire central, sans extraction).
/// Sert au planificateur pour décider du rangement extrait et au calcul de taille.
/// </summary>
public sealed class ArchiveInfo
{
    /// <summary>Chemin absolu de l'archive inspectée.</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>Taille de l'archive sur le disque.</summary>
    public long CompressedSize { get; init; }

    /// <summary>Somme des tailles décompressées des entrées fichier.</summary>
    public long UncompressedSize { get; init; }

    /// <summary>Entrées fichier de l'archive (les entrées dossier sont ignorées), chemin interne tel quel.</summary>
    public IReadOnlyList<ArchiveEntryInfo> Entries { get; init; } = [];

    /// <summary>True si l'archive a pu être lue. False : format non géré ou fichier corrompu — elle sera copiée telle quelle.</summary>
    public bool IsReadable { get; init; }

    /// <summary>True si toutes les entrées sont à la racine de l'archive (aucun sous-dossier interne).</summary>
    public bool IsFlat => Entries.All(e => !e.InternalPath.Contains('/') && !e.InternalPath.Contains('\\'));
}

/// <summary>Une entrée fichier d'une archive.</summary>
public sealed class ArchiveEntryInfo
{
    /// <summary>Chemin interne de l'entrée, séparateurs tels que stockés dans l'archive.</summary>
    public string InternalPath { get; init; } = string.Empty;

    /// <summary>Nom de fichier seul (dernier segment).</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>Taille décompressée.</summary>
    public long Length { get; init; }
}
