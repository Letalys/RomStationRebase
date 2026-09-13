using System.Threading;

namespace RomStationRebase.Models;

/// <summary>Paramètres d'exécution d'un rebase transmis à RebaseService.RunRebaseAsync.</summary>
public class RebaseOptions
{
    /// <summary>Plan calculé par RebasePlanner : noms, emplacements, M3U, jaquettes et entrées gamelist de chaque jeu.</summary>
    public RebasePlan             Plan              { get; set; } = new();
    public string                 TargetPath        { get; set; } = string.Empty;
    public ArchitectureEntry      Architecture      { get; set; } = null!;
    public DuplicatePolicy        DuplicatePolicy   { get; set; } = DuplicatePolicy.Ignore;
    public int                    MaxParallelCopies { get; set; } = 4;
    public int                    RetryCount        { get; set; } = 2;
    public int                    RetryDelaySeconds { get; set; } = 3;

    /// <summary>Écrire le fichier de métadonnées de chaque dossier système en fin de rebase.</summary>
    public bool GenerateGamelist { get; set; }

    /// <summary>Copier un gamelist existant en gamelist.xml.yyyyMMdd avant de le fusionner.</summary>
    public bool BackupGamelist { get; set; }

    /// <summary>Métadonnées par identifiant de jeu, déjà dans la langue choisie. Null si aucun gamelist n'est demandé.</summary>
    public IReadOnlyDictionary<int, GameMetadata>? Metadata { get; set; }

    /// <summary>Événement de pause — Reset() pour mettre en pause, Set() pour reprendre.</summary>
    public ManualResetEventSlim   PauseEvent        { get; set; } = new ManualResetEventSlim(true);
}
