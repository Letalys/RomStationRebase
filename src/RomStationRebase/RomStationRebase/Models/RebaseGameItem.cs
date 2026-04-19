namespace RomStationRebase.Models;

/// <summary>Représente un jeu dans la file d'exécution du rebase, avec ses chemins sources et son état de copie.</summary>
public class RebaseGameItem
{
    public int             GameId          { get; set; }
    public string          Title           { get; set; } = string.Empty;
    public string          SystemName      { get; set; } = string.Empty;
    public string?         SystemImagePath { get; set; }
    public int             FileCount       { get; set; }
    public List<string>    SourceFilePaths { get; set; } = [];
    public RebaseItemStatus Status         { get; set; } = RebaseItemStatus.Pending;
    public double          Progress        { get; set; }
    public string?         ErrorDetail     { get; set; }
    public bool            IsSkipped       { get; set; }
}

/// <summary>État d'un jeu dans la progression du rebase.</summary>
public enum RebaseItemStatus
{
    Pending,
    Copying,
    Done,
    Skipped,
    Failed
}
