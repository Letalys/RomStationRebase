using System.Threading;
using RomStationRebase.Services;

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

    /// <summary>
    /// Outils externes disponibles pour ce rebase, par identifiant : le descripteur et l'exécutable choisi par l'utilisateur.
    /// Tout fichier du plan de genre Transform désigne l'un d'eux.
    /// </summary>
    public IReadOnlyDictionary<string, (ExternalTool Tool, string ExecutablePath)> Tools { get; set; }
        = new Dictionary<string, (ExternalTool, string)>();

    /// <summary>Dossier de travail des conversions, sur le disque local.</summary>
    public string WorkDirectory { get; set; } = string.Empty;

    /// <summary>Journal détaillé du rebase. Muet par défaut.</summary>
    public IRebaseLog Log { get; set; } = NullRebaseLog.Instance;

    /// <summary>Événement de pause — Reset() pour mettre en pause, Set() pour reprendre.</summary>
    public ManualResetEventSlim   PauseEvent        { get; set; } = new ManualResetEventSlim(true);
}
