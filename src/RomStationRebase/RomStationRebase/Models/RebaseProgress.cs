namespace RomStationRebase.Models;

/// <summary>Snapshot de progression transmis par IProgress&lt;RebaseProgress&gt; à chaque mise à jour.</summary>
public class RebaseProgress
{
    public int      TotalFiles              { get; set; }
    public int      CompletedFiles          { get; set; }
    public int      FailedFiles             { get; set; }
    public int      SkippedFiles            { get; set; }
    public long     TotalBytes              { get; set; }
    public long     CopiedBytes             { get; set; }
    public double   SpeedBytesPerSecond     { get; set; }
    public TimeSpan EstimatedTimeRemaining  { get; set; }
    /// <summary>Dernier jeu dont le statut vient de changer — null pour les rapports agrégés périodiques.</summary>
    public RebaseGameItem? CurrentItem { get; set; }
}
