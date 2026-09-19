namespace RomStationRebase.Models;

/// <summary>
/// Fichier de sélection (*.rsr, contenu JSON) : les jeux cochés et les paramètres du rebase, pour retrouver
/// une configuration de travail sans tout recocher. Lisible et modifiable à la main.
/// </summary>
public sealed class RebasePreset
{
    /// <summary>Signature du format : un JSON quelconque n'est pas une sélection.</summary>
    public const string FormatId = "romstation-rebase-preset";

    /// <summary>Signature des tout premiers fichiers, du temps où la présélection s'appelait « sélection » puis « projet ». Toujours acceptée à la lecture.</summary>
    public static readonly IReadOnlyList<string> LegacyFormatIds = ["romstation-rebase-project", "romstation-rebase-selection"];

    /// <summary>Version du format que cette version de l'application écrit et comprend entièrement.</summary>
    public const int CurrentVersion = 1;

    /// <summary>Vide tant que le fichier n'a pas été lu ou écrit : un JSON étranger, sans ce champ, ne doit pas passer pour une sélection.</summary>
    public string   Format     { get; set; } = string.Empty;
    public int      Version    { get; set; } = CurrentVersion;
    /// <summary>Version de l'application qui a écrit le fichier, pour le diagnostic.</summary>
    public string?  AppVersion { get; set; }
    public DateTime SavedAt    { get; set; }

    public RebasePresetSettings   Settings { get; set; } = new();
    public List<RebasePresetGame> Games    { get; set; } = [];
}

/// <summary>
/// Un jeu de la sélection. L'identifiant RomStation (rid) est stable d'une base à l'autre, contrairement
/// à l'identifiant Derby ; le titre et le système servent aux messages et de repli si le rid a changé.
/// </summary>
public sealed class RebasePresetGame
{
    public int    Rid    { get; set; }
    public string Title  { get; set; } = string.Empty;
    public string System { get; set; } = string.Empty;

    // Règles basculées pour ce seul jeu dans le tableau du rebase. Null : la valeur de l'architecture.
    public bool? KeepFileName { get; set; }
    public bool? M3U          { get; set; }
    public bool? Extract      { get; set; }

    [global::System.Text.Json.Serialization.JsonIgnore]
    public bool HasOverride => KeepFileName is not null || M3U is not null || Extract is not null;
}

/// <summary>Paramètres de la fenêtre de rebase, tels qu'ils étaient à l'enregistrement.</summary>
public sealed class RebasePresetSettings
{
    public string TargetPath        { get; set; } = string.Empty;
    public string ArchitectureId    { get; set; } = string.Empty;
    /// <summary>Libellé de l'architecture : seulement pour nommer celle qui aurait disparu.</summary>
    public string ArchitectureLabel { get; set; } = string.Empty;
    /// <summary>"ExtractRequired", "Copy" ou "ExtractAll" (valeurs de <see cref="Models.ArchiveMode"/>).</summary>
    public string ArchiveMode       { get; set; } = "ExtractRequired";
    /// <summary>"Auto" ou "Subfolder".</summary>
    public string ExtractLayout     { get; set; } = "Auto";
    public bool   CopyCovers        { get; set; }
    public bool   GenerateGamelist  { get; set; }
    public bool   BackupGamelist    { get; set; }
    /// <summary>"fr", "en" ou "auto" (langue de l'interface).</summary>
    public string MetadataLanguage  { get; set; } = "auto";
    /// <summary>"Ignore" ou "Overwrite".</summary>
    public string DuplicatePolicy   { get; set; } = "Ignore";
    public int    MaxParallelCopies { get; set; } = 4;
    public int    RetryCount        { get; set; } = 3;
    public int    RetryDelaySeconds { get; set; } = 5;

    public RebasePresetSettings Clone() => (RebasePresetSettings)MemberwiseClone();
}

/// <summary>Jeu de la bibliothèque, réduit à ce qu'il faut pour le rapprocher d'un fichier de sélection.</summary>
public sealed record LibraryGameRef(int Id, int Rid, string Title, string System);

/// <summary>Résultat du rapprochement d'un fichier de sélection avec la bibliothèque courante.</summary>
public sealed class RebasePresetMatch
{
    /// <summary>Identifiant Derby du jeu retrouvé → son entrée dans le fichier.</summary>
    public Dictionary<int, RebasePresetGame> Matched { get; } = [];

    /// <summary>Jeux du fichier absents de la bibliothèque.</summary>
    public List<RebasePresetGame> Missing { get; } = [];

    /// <summary>Jeux retrouvés par leur titre et leur système, leur identifiant RomStation ayant changé.</summary>
    public List<RebasePresetGame> MatchedByTitle { get; } = [];
}
