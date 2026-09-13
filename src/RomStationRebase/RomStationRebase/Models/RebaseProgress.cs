namespace RomStationRebase.Models;

/// <summary>Phase courante du rebase.</summary>
public enum RebasePhase
{
    /// <summary>Copie et extraction des fichiers, jaquettes comprises.</summary>
    Transferring,
    /// <summary>Écriture des fichiers de métadonnées, après la dernière copie.</summary>
    WritingMetadata,
    /// <summary>Tout est terminé.</summary>
    Completed
}

/// <summary>Snapshot de progression transmis par IProgress&lt;RebaseProgress&gt; à chaque mise à jour.</summary>
public class RebaseProgress
{
    public RebasePhase Phase                   { get; set; } = RebasePhase.Transferring;
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

    /// <summary>Nombre de dossiers système dont le fichier de métadonnées a été écrit (phase Completed).</summary>
    public int MetadataFoldersWritten { get; set; }

    /// <summary>Messages non bloquants de la phase métadonnées : sauvegardes de fichiers illisibles, erreurs d'écriture.</summary>
    public IReadOnlyList<string> MetadataNotes { get; set; } = [];
}
