using System.Threading;
using RomStationRebase.ViewModels;

namespace RomStationRebase.Models;

/// <summary>Paramètres d'exécution d'un rebase transmis à RebaseService.RunRebaseAsync.</summary>
public class RebaseOptions
{
    public List<GameItemViewModel> SelectedGames    { get; set; } = [];
    public string                 TargetPath        { get; set; } = string.Empty;
    public ArchitectureEntry      Architecture      { get; set; } = null!;
    public FolderTreeMapping      Mapping           { get; set; } = null!;
    public bool                   GenerateM3U       { get; set; }
    public DuplicatePolicy        DuplicatePolicy   { get; set; } = DuplicatePolicy.Ignore;
    public int                    MaxParallelCopies { get; set; } = 4;
    public int                    RetryCount        { get; set; } = 2;
    public int                    RetryDelaySeconds { get; set; } = 3;
    public string                 RomStationPath    { get; set; } = string.Empty;
    /// <summary>Événement de pause — Reset() pour mettre en pause, Set() pour reprendre.</summary>
    public ManualResetEventSlim   PauseEvent        { get; set; } = new ManualResetEventSlim(true);
}
