namespace RomStationRebase.Models;

/// <summary>Représente un jeu dans la file d'exécution du rebase, avec son plan et son état de copie.</summary>
public class RebaseGameItem
{
    public int             GameId          { get; set; }
    public string          Title           { get; set; } = string.Empty;
    public string          SystemName      { get; set; } = string.Empty;
    public string?         SystemImagePath { get; set; }
    public int             FileCount       { get; set; }
    /// <summary>Plan du jeu : fichiers, M3U, jaquettes, entrées gamelist.</summary>
    public RebaseGamePlan  Plan            { get; set; } = new();
    public RebaseItemStatus Status         { get; set; } = RebaseItemStatus.Pending;
    public double          Progress        { get; set; }
    /// <summary>Message d'erreur (statut Failed) ou avertissement non bloquant (jaquette illisible…).</summary>
    public string?         ErrorDetail     { get; set; }
    public bool            IsSkipped       { get; set; }
}

/// <summary>État d'un jeu dans la progression du rebase.</summary>
public enum RebaseItemStatus
{
    Pending,
    Copying,
    Extracting,
    Done,
    Skipped,
    Failed
}
