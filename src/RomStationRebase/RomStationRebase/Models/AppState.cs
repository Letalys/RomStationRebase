namespace RomStationRebase.Models;

/// <summary>État persistant de l'application — chemin RomStation, versions, dernière synchronisation.</summary>
public class AppState
{
    /// <summary>Chemin du dossier d'installation de RomStation.</summary>
    public string? RomStationPath { get; set; }

    /// <summary>Version de RomStation lue dans RomStation.cfg (app.version).</summary>
    public string? RomStationVersion { get; set; }

    /// <summary>Version de Derby extraite du classpath RomStation.</summary>
    public string? DerbyVersion { get; set; }

    /// <summary>Chemin local de la copie de travail de la base Derby.</summary>
    public string? DatabaseCopyPath { get; set; }

    /// <summary>Date et heure de la dernière synchronisation réussie.</summary>
    public DateTime? LastSyncDate { get; set; }

    /// <summary>Nombre de jeux chargés lors de la dernière synchronisation.</summary>
    public int LastSyncGameCount { get; set; }
}
