using RomStationRebase.Models;

namespace RomStationRebase.ViewModels;

/// <summary>
/// Ligne éditable de la table des systèmes d'une architecture — miroir observable d'un SystemMapping.
/// Les modifications sont poussées à chaque frappe, le brouillon parent en déduit son état modifié.
/// </summary>
public class SystemMappingRowViewModel : ViewModelBase
{
    private string _romStationSystem;
    private string _targetFolder;
    private bool   _keepFileName;
    private bool   _m3u;
    private bool   _extract;
    private IReadOnlyList<string> _availableSystems = [];

    /// <summary>
    /// Systèmes RomStation proposés dans la liste déroulante de cette ligne : tous ceux de la base,
    /// moins ceux déjà paramétrés sur une autre ligne. Recalculé par le brouillon parent.
    /// </summary>
    public IReadOnlyList<string> AvailableSystems
    {
        get => _availableSystems;
        internal set => SetProperty(ref _availableSystems, value);
    }

    /// <summary>Ligne vide, ajoutée par le bouton "Ajouter un système".</summary>
    public SystemMappingRowViewModel() : this(new SystemMapping()) { }

    public SystemMappingRowViewModel(SystemMapping mapping)
    {
        _romStationSystem = mapping.RomStationSystem;
        _targetFolder     = mapping.TargetFolder;
        _keepFileName     = mapping.KeepFileName;
        _m3u              = mapping.M3U;
        _extract          = mapping.Extract;
    }

    /// <summary>Nom du système tel qu'il apparaît dans la base RomStation.</summary>
    public string RomStationSystem
    {
        get => _romStationSystem;
        set => SetProperty(ref _romStationSystem, value ?? string.Empty);
    }

    /// <summary>Nom du dossier cible (ex : "psx").</summary>
    public string TargetFolder
    {
        get => _targetFolder;
        set => SetProperty(ref _targetFolder, value ?? string.Empty);
    }

    /// <summary>Romset : jamais renommé, jamais extrait, jamais de M3U.</summary>
    public bool KeepFileName
    {
        get => _keepFileName;
        set => SetProperty(ref _keepFileName, value);
    }

    /// <summary>L'émulateur lit les playlists M3U sur cette cible.</summary>
    public bool M3U
    {
        get => _m3u;
        set => SetProperty(ref _m3u, value);
    }

    /// <summary>L'émulateur ne lit pas les archives : extraction requise.</summary>
    public bool Extract
    {
        get => _extract;
        set => SetProperty(ref _extract, value);
    }

    /// <summary>Modèle sérialisable, valeurs texte nettoyées des espaces.</summary>
    public SystemMapping ToModel() => new()
    {
        RomStationSystem = _romStationSystem.Trim(),
        TargetFolder     = _targetFolder.Trim(),
        KeepFileName     = _keepFileName,
        M3U              = _m3u,
        Extract          = _extract,
    };
}
