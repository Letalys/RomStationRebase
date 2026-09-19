namespace RomStationRebase.Models;

/// <summary>
/// Index des architectures cibles, désérialisé depuis config/architectures/index.json (distribué)
/// ou depuis l'index utilisateur de %LOCALAPPDATA%. Seul l'index utilisateur porte HiddenDistributed.
/// </summary>
public class ArchitectureIndex
{
    public List<ArchitectureEntry> Architectures { get; set; } = [];

    /// <summary>
    /// Identifiants des architectures par défaut déjà copiées dans le dossier utilisateur. Un défaut supprimé y reste :
    /// il n'est pas recopié au lancement suivant, seule la restauration explicite le ramène.
    /// </summary>
    public List<string> SeededDefaults { get; set; } = [];
}

/// <summary>
/// Entrée d'une architecture cible : identifiant, libellé, fichier de mapping et défauts de sortie.
/// Les règles par système (romset, M3U, extraction) vivent dans le mapping, pas ici.
/// </summary>
public class ArchitectureEntry
{
    public string Id                  { get; set; } = string.Empty;
    public string Label               { get; set; } = string.Empty;
    public string Description         { get; set; } = string.Empty;
    public string FolderTreeMapping   { get; set; } = string.Empty;
    public bool   IsDefault           { get; set; }

    // ── Défauts de sortie 1.3.0 ──────────────────────────────────────────

    /// <summary>Copier les jaquettes par défaut.</summary>
    public bool CoversByDefault { get; set; }

    /// <summary>Dossier des jaquettes, relatif au dossier système (ex : "images", "Imgs"). Avec le marqueur {system}, relatif à la destination (ex : "ES-DE/downloaded_media/{system}/covers").</summary>
    public string CoverFolder { get; set; } = "images";

    /// <summary>Suffixe ajouté au nom de la ROM pour nommer la jaquette (ex : "-image" pour l'art local d'EmulationStation).</summary>
    public string CoverSuffix { get; set; } = string.Empty;

    /// <summary>Largeur maximale des jaquettes copiées, 0 = taille d'origine.</summary>
    public int CoverMaxWidth { get; set; }

    /// <summary>Hauteur maximale des jaquettes copiées, 0 = taille d'origine.</summary>
    public int CoverMaxHeight { get; set; }

    /// <summary>Format du fichier de métadonnées, un identifiant de <see cref="MetadataFormats"/>, ou null si la cible n'en lit aucun.</summary>
    public string? GamelistFormat { get; set; }

    /// <summary>Générer le fichier de métadonnées par défaut.</summary>
    public bool GamelistByDefault { get; set; }

    /// <summary>True si la cible sait lire un fichier de métadonnées. Dérivé, jamais sérialisé.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool SupportsGamelist => MetadataFormats.IsKnown(GamelistFormat);

    /// <summary>Provenance de l'entrée, posée par ArchitectureService au chargement. Jamais sérialisée.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public ArchitectureOrigin Origin { get; set; } = ArchitectureOrigin.Distributed;

    /// <summary>Copie profonde des champs sérialisés — sert au brouillon de l'éditeur d'architectures.</summary>
    public ArchitectureEntry Clone() => new()
    {
        Id = Id, Label = Label, Description = Description, FolderTreeMapping = FolderTreeMapping,
        IsDefault = IsDefault, CoversByDefault = CoversByDefault,
        CoverFolder = CoverFolder, CoverSuffix = CoverSuffix,
        CoverMaxWidth = CoverMaxWidth, CoverMaxHeight = CoverMaxHeight,
        GamelistFormat = GamelistFormat, GamelistByDefault = GamelistByDefault,
        Origin = Origin,
    };
}

/// <summary>D'où vient une architecture : fichiers distribués, distribuée mais modifiée par l'utilisateur, ou créée par lui.</summary>
public enum ArchitectureOrigin
{
    Distributed,
    Overridden,
    Custom
}
