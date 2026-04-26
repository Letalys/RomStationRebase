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

    /// <summary>Date UTC du dernier check de MAJ. Null = jamais vérifié.</summary>
    public DateTime? LastUpdateCheckUtc { get; set; }

    /// <summary>Tag de la dernière release distante disponible (ex : "v1.2.0"). Null = à jour ou jamais vérifié.</summary>
    public string? LastAvailableVersion { get; set; }

    /// <summary>URL html_url de la release disponible. Null si à jour ou jamais vérifié.</summary>
    public string? LastUpdateUrl { get; set; }
}
