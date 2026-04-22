namespace RomStationRebase.Models;

/// <summary>Préférences utilisateur persistantes — langue, politique de doublons, parallélisme.</summary>
public class UserPreferences
{
    /// <summary>Langue de l'interface : "auto" (détection système), "en" ou "fr".</summary>
    public string AppLanguage { get; set; } = "auto";

    /// <summary>Comportement en cas de doublon lors du rebase : "Ignore" ou "Overwrite".</summary>
    public string DuplicatePolicy { get; set; } = "Ignore";

    /// <summary>Nombre de threads de copie simultanés (1 à 16).</summary>
    public int MaxParallelCopies { get; set; } = 4;

    /// <summary>Nombre de tentatives de copie en cas d'échec (0 = aucun retry).</summary>
    public int RetryCount { get; set; } = 2;

    /// <summary>Délai en secondes entre deux tentatives de copie.</summary>
    public int RetryDelaySeconds { get; set; } = 3;

    /// <summary>Bounds mémorisés de MainWindow. Null au premier lancement.</summary>
    public WindowBounds? MainWindowBounds { get; set; }

    /// <summary>Bounds mémorisés de RebaseWindow. Null au premier lancement.</summary>
    public WindowBounds? RebaseWindowBounds { get; set; }

    /// <summary>Dernier mode d'affichage utilisé dans MainWindow : "Mosaic" ou "List". Défaut : "Mosaic".</summary>
    public string LastViewMode { get; set; } = "Mosaic";

    /// <summary>Thème d'interface : "Light" uniquement pour l'instant (seul thème implémenté). Défaut : "Light".</summary>
    public string Theme { get; set; } = "Light";

    /// <summary>Dernier dossier de destination utilisé dans RebaseWindow. Vide au premier lancement.</summary>
    public string LastRebaseTargetPath { get; set; } = string.Empty;

    /// <summary>Identifiant de la dernière architecture sélectionnée (ex : "retroarch", "lakka", "arkos"). Vide au premier lancement.</summary>
    public string LastRebaseArchitectureId { get; set; } = string.Empty;

    /// <summary>Dernière valeur du toggle "Générer M3U" dans RebaseWindow.</summary>
    public bool LastRebaseGenerateM3U { get; set; } = false;

    /// <summary>Taille des vignettes de jeu dans MainWindow : "Normal" (défaut, dimensions actuelles) ou "Grand".</summary>
    public string ThumbnailSize { get; set; } = "Normal";

    /// <summary>Critère de tri global sélectionné par l'utilisateur. Valeurs : "Title" (défaut) ou "System".</summary>
    public string LastSortCriteria { get; set; } = "Title";
}
