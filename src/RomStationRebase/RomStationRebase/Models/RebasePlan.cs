namespace RomStationRebase.Models;

/// <summary>Traitement des archives lors du rebase.</summary>
public enum ArchiveMode
{
    /// <summary>Les archives sont copiées telles quelles (comportement 1.2.0).</summary>
    Copy,
    /// <summary>Les archives sont extraites pour les systèmes marqués "extract" dans l'architecture cible.</summary>
    ExtractRequired,
    /// <summary>Toutes les archives sont extraites, sauf celles des systèmes arcade (romsets).</summary>
    ExtractAll
}

/// <summary>Rangement des fichiers extraits dans le dossier système.</summary>
public enum ExtractLayout
{
    /// <summary>A plat si l'archive ne contient qu'un fichier, sous-dossier par jeu sinon.</summary>
    Auto,
    /// <summary>Toujours un sous-dossier par jeu.</summary>
    Subfolder
}

/// <summary>Nature du transfert d'un fichier source vers la cible.</summary>
public enum FileTransferKind
{
    /// <summary>Copie du fichier sous son nom cible.</summary>
    Copy,
    /// <summary>Extraction de l'archive vers le dossier cible.</summary>
    Extract,
    /// <summary>Copie récursive du dossier du fichier (jeux DOS ou Windows déjà installés).</summary>
    CopyTree,
    /// <summary>
    /// Conversion par un outil externe : extraction dans un dossier de travail local, conversion sur place,
    /// puis copie du seul résultat vers la cible.
    /// </summary>
    Transform
}

/// <summary>Résumé du traitement prévu pour un jeu — affiché dans la fenêtre de rebase.</summary>
public enum GameOutputKind
{
    /// <summary>Copie simple.</summary>
    Copy,
    /// <summary>Romset arcade copié sous son nom d'origine.</summary>
    Romset,
    /// <summary>Plusieurs disques réunis par un M3U.</summary>
    DiscSet,
    /// <summary>Plusieurs disques sans M3U.</summary>
    Discs,
    /// <summary>Plusieurs versions du même jeu.</summary>
    Variants,
    /// <summary>Archive(s) extraite(s).</summary>
    Extract,
    /// <summary>Fichier(s) converti(s) par un outil externe.</summary>
    Converted,
    /// <summary>Dossier de jeu copié tel quel.</summary>
    Folder,
    /// <summary>Système sans correspondance dans l'architecture : ignoré.</summary>
    Unmapped
}

/// <summary>Plan de transfert d'un fichier source.</summary>
public sealed class RebaseFilePlan
{
    /// <summary>Chemin absolu du fichier source.</summary>
    public string SourcePath { get; init; } = string.Empty;

    /// <summary>Libellé Derby du fichier (APP.GAME_FILE.NAME).</summary>
    public string Label { get; init; } = string.Empty;

    public FileTransferKind Kind { get; init; }

    /// <summary>
    /// Chemin cible du fichier lançable, relatif au dossier système, séparateur "/".
    /// Copy : le fichier copié. Extract : l'entrée principale extraite. CopyTree : le fichier de profil dans le dossier copié.
    /// </summary>
    public string LaunchRelativePath { get; init; } = string.Empty;

    /// <summary>Dossier de dépôt relatif au dossier système ("" = à plat). Renseigné pour Extract et CopyTree.</summary>
    public string DestRelativeDir { get; init; } = string.Empty;

    /// <summary>Extract à plat d'une archive à entrée unique : nom cible de l'entrée. Null si les noms internes sont conservés.</summary>
    public string? SingleEntryTargetName { get; init; }

    /// <summary>
    /// Image disque brute extraite sans son descripteur : chemin du .cue à écrire après l'extraction,
    /// relatif au dossier système. C'est alors lui le fichier lançable. Null dans tous les autres cas.
    /// </summary>
    public string? CueRelativePath { get; init; }

    /// <summary>Chemin du .bin que décrit le .cue généré, relatif au dossier système. Même dossier que le .cue.</summary>
    public string? CueBinRelativePath { get; init; }

    /// <summary>Dossier source à copier pour CopyTree (absolu).</summary>
    public string? SourceDirectory { get; init; }

    /// <summary>Transform : identifiant de l'outil externe à lancer.</summary>
    public string? TransformToolId { get; init; }

    /// <summary>
    /// Transform : chemin interne, dans l'archive source, du fichier à donner à l'outil (le .gdi, le .cue, l'.iso).
    /// Null quand la source n'est pas une archive : c'est alors elle qui est convertie.
    /// </summary>
    public string? TransformInputEntry { get; init; }

    /// <summary>Transform : taille attendue du résultat, d'après le ratio indicatif de l'outil. Une estimation.</summary>
    public long TransformedSizeEstimate { get; init; }

    /// <summary>Archive inspectée, pour Extract.</summary>
    public ArchiveInfo? Archive { get; init; }

    /// <summary>Taille à copier (octets sur disque du ou des fichiers source).</summary>
    public long CopySize { get; init; }

    /// <summary>Taille une fois extraite (Extract uniquement, sinon égale à CopySize).</summary>
    public long ExtractedSize { get; init; }

    /// <summary>Octets qui seront écrits sur la cible.</summary>
    public long PlannedBytes => Kind switch
    {
        FileTransferKind.Extract   => ExtractedSize,
        FileTransferKind.Transform => TransformedSizeEstimate,
        _                          => CopySize,
    };

    /// <summary>Place nécessaire dans le dossier de travail local pendant une conversion : l'entrée extraite et le résultat.</summary>
    public long WorkBytes => Kind == FileTransferKind.Transform ? ExtractedSize + TransformedSizeEstimate : 0;
}

/// <summary>Jaquette à copier.</summary>
public sealed class RebaseCoverPlan
{
    public string SourcePath       { get; init; } = string.Empty;
    /// <summary>Chemin cible, séparateur "/" : relatif au dossier système, ou à la destination si <see cref="IsRootRelative"/>.</summary>
    public string DestRelativePath { get; init; } = string.Empty;
    /// <summary>
    /// True quand le dossier des jaquettes de l'architecture porte le marqueur {system} : la jaquette sort de
    /// l'arborescence des ROMs (ES-DE/downloaded_media/psx/covers) et son chemin part de la destination.
    /// </summary>
    public bool IsRootRelative { get; init; }
}

/// <summary>Entrée à écrire dans le fichier de métadonnées du dossier système.</summary>
public sealed class GamelistEntryPlan
{
    /// <summary>Chemin relatif au dossier système, préfixé "./" (convention EmulationStation).</summary>
    public string Path  { get; init; } = string.Empty;
    /// <summary>Nom affiché : le titre du jeu, ou le libellé du fichier pour un jeu à plusieurs fichiers sans M3U.</summary>
    public string Name  { get; init; } = string.Empty;
    /// <summary>Chemin de la jaquette relatif au dossier système, préfixé "./". Null si aucune jaquette n'est copiée.</summary>
    public string? Image { get; init; }
}

/// <summary>Plan complet d'un jeu.</summary>
public sealed class RebaseGamePlan
{
    public int     GameId          { get; init; }
    public string  Title           { get; init; } = string.Empty;
    public string  SystemName      { get; init; } = string.Empty;
    public string? SystemImagePath { get; init; }

    /// <summary>Dossier système cible relatif à la destination. Null si le système n'a pas de correspondance.</summary>
    public string? TargetFolder { get; init; }

    /// <summary>Nom de base unique du jeu sur son système (titre nettoyé, départagé si homonyme).</summary>
    public string BaseName { get; init; } = string.Empty;

    public GameOutputKind OutputKind { get; init; }

    public IReadOnlyList<RebaseFilePlan>     Files           { get; init; } = [];
    /// <summary>Chemin du M3U relatif au dossier système, null si aucun.</summary>
    public string?                           M3URelativePath { get; init; }
    /// <summary>Lignes du M3U, chemins relatifs au dossier système.</summary>
    public IReadOnlyList<string>             M3UEntries      { get; init; } = [];
    public IReadOnlyList<RebaseCoverPlan>    Covers          { get; init; } = [];
    public IReadOnlyList<GamelistEntryPlan>  GamelistEntries { get; init; } = [];

    public bool IsUnmapped => TargetFolder is null;

    /// <summary>True si les fichiers du jeu sont de vrais disques numérotés (et non des versions d'un même jeu) : seul cas où un M3U a un objet.</summary>
    public bool IsDiscSet { get; init; }

    /// <summary>True si au moins un fichier du jeu est une archive : sans archive, l'extraction n'a pas d'objet.</summary>
    public bool HasArchive { get; init; }

    /// <summary>
    /// Extensions de ce qu'un outil de conversion recevrait pour ce jeu (l'entrée principale de chaque archive, ou le fichier
    /// lui-même). Vide pour un romset ou un jeu en dossier. Sert à ne proposer, jeu par jeu, que les outils qui conviennent.
    /// </summary>
    public IReadOnlyList<string> ToolInputExtensions { get; init; } = [];

    /// <summary>Octets prévus pour ce jeu, jaquettes comprises.</summary>
    public long PlannedBytes { get; init; }

    /// <summary>Chemins cibles produits, relatifs à la destination — pour l'infobulle de la fenêtre de rebase.</summary>
    public IReadOnlyList<string> OutputPaths { get; init; } = [];
}

/// <summary>Plan complet du rebase, calculé avant le démarrage et réutilisé par RebaseService.</summary>
public sealed class RebasePlan
{
    public IReadOnlyList<RebaseGamePlan> Games { get; init; } = [];
    public long TotalBytes => Games.Sum(g => g.PlannedBytes);

    /// <summary>Place à prévoir dans le dossier de travail : la plus grosse conversion, elles passent une à la fois.</summary>
    public long MaxWorkBytes => Games.SelectMany(g => g.Files).Select(f => f.WorkBytes).DefaultIfEmpty(0).Max();

    /// <summary>Outils demandés par l'architecture mais indisponibles (exécutable non indiqué) : libellés, sans doublon.</summary>
    public IReadOnlyList<string> UnavailableTools { get; init; } = [];
}
