using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using RomStationRebase.Models;
using RomStationRebase.Resources;

namespace RomStationRebase.ViewModels;

/// <summary>
/// Brouillon d'une architecture dans l'éditeur : copie de l'entrée d'index et table des systèmes,
/// chargée paresseusement à la première sélection. Toute édition passe par ce brouillon — les
/// fichiers ne sont écrits qu'au moment d'Enregistrer.
/// </summary>
public class ArchitectureDraftViewModel : ViewModelBase
{
    private ArchitectureEntry _entry;
    private bool   _isDirty;
    private bool   _suspendDirty;
    private bool   _mappingsLoaded;
    private string _coverMaxWidthText;
    private string _coverMaxHeightText;
    private bool   _coverMaxWidthValid  = true;
    private bool   _coverMaxHeightValid = true;

    // Lignes dont on écoute PropertyChanged — pour les détacher proprement au retrait ou au Clear
    private readonly List<SystemMappingRowViewModel> _trackedRows = new();

    private IReadOnlyList<string> _allSystems = [];

    /// <summary>Tous les systèmes RomStation de la base, posés par l'éditeur ; chaque ligne en propose ceux qui restent libres.</summary>
    public IReadOnlyList<string> AllSystems
    {
        get => _allSystems;
        set { _allSystems = value; RefreshAvailableSystems(); }
    }

    private IReadOnlyDictionary<string, string>? _systemIcons;

    /// <summary>Icônes des consoles par nom de système, posées par l'éditeur et transmises à chaque ligne.</summary>
    public IReadOnlyDictionary<string, string>? SystemIcons
    {
        get => _systemIcons;
        set { _systemIcons = value; RefreshAvailableSystems(); }
    }

    /// <summary>
    /// Recalcule la liste déroulante de chaque ligne : les systèmes non encore paramétrés sur une autre ligne.
    /// Un système déjà pris ne peut donc être choisi qu'une fois, ce qui évite les doublons avant même la validation.
    /// </summary>
    private void RefreshAvailableSystems()
    {
        var used = Mappings
            .Select(r => r.RomStationSystem.Trim())
            .Where(s => s.Length > 0)
            .ToList();

        foreach (var row in Mappings)
        {
            row.Icons  = _systemIcons;
            string own = row.RomStationSystem.Trim();
            row.AvailableSystems = _allSystems
                .Where(s => string.Equals(s, own, StringComparison.OrdinalIgnoreCase)
                            || !used.Contains(s, StringComparer.OrdinalIgnoreCase))
                .ToList();
        }
    }

    /// <param name="source">Entrée d'index, clonée : la liste d'origine n'est jamais modifiée.</param>
    /// <param name="isPersisted">False pour une création de la session, tant qu'elle n'a pas été écrite.</param>
    public ArchitectureDraftViewModel(ArchitectureEntry source, bool isPersisted = true)
    {
        _entry              = source.Clone();
        IsPersisted         = isPersisted;
        _coverMaxWidthText  = _entry.CoverMaxWidth.ToString(CultureInfo.InvariantCulture);
        _coverMaxHeightText = _entry.CoverMaxHeight.ToString(CultureInfo.InvariantCulture);
        Mappings.CollectionChanged += OnMappingsChanged;
    }

    /// <summary>Déclenché quand IsDirty change — l'éditeur s'en sert pour rafraîchir ses états dérivés.</summary>
    public event Action? DirtyChanged;

    // ── Identité ──────────────────────────────────────────────────────────

    /// <summary>Entrée d'index du brouillon (clone). C'est cet objet qui est sérialisé à l'enregistrement.</summary>
    public ArchitectureEntry Entry => _entry;

    public string Id => _entry.Id;

    public ArchitectureOrigin Origin => _entry.Origin;

    /// <summary>Badge de provenance affiché dans la liste.</summary>
    public string OriginText => _entry.Origin switch
    {
        ArchitectureOrigin.Overridden => Strings.ArchEditor_Origin_Overridden,
        ArchitectureOrigin.Custom     => Strings.ArchEditor_Origin_Custom,
        _                             => Strings.ArchEditor_Origin_Distributed,
    };

    /// <summary>Libellé, ou identifiant si le libellé est vide — pour les messages.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(_entry.Label) ? _entry.Id : _entry.Label;

    /// <summary>False tant qu'une création de la session n'a pas été écrite sur disque.</summary>
    public bool IsPersisted { get; private set; }

    /// <summary>True si le brouillon diffère de ce qui est sur disque.</summary>
    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (SetProperty(ref _isDirty, value))
                DirtyChanged?.Invoke();
        }
    }

    public bool MappingsLoaded => _mappingsLoaded;

    /// <summary>Table des systèmes, vide tant que LoadMappings n'a pas été appelé.</summary>
    public ObservableCollection<SystemMappingRowViewModel> Mappings { get; } = new();

    // ── Général ───────────────────────────────────────────────────────────

    public string Label
    {
        get => _entry.Label;
        set => SetEntryValue(_entry.Label, value ?? string.Empty, v => _entry.Label = v, nameof(DisplayName));
    }

    public string Description
    {
        get => _entry.Description;
        set => SetEntryValue(_entry.Description, value ?? string.Empty, v => _entry.Description = v);
    }

    // ── Jaquettes ─────────────────────────────────────────────────────────

    /// <summary>Fichiers des jeux à plusieurs fichiers rangés dans un dossier masqué, lancé par un M3U.</summary>
    public bool HideGameFiles
    {
        get => _entry.HideGameFiles;
        set => SetEntryValue(_entry.HideGameFiles, value, v => _entry.HideGameFiles = v);
    }

    public bool CoversByDefault
    {
        get => _entry.CoversByDefault;
        set => SetEntryValue(_entry.CoversByDefault, value, v => _entry.CoversByDefault = v);
    }

    public string CoverFolder
    {
        get => _entry.CoverFolder;
        set => SetEntryValue(_entry.CoverFolder, value ?? string.Empty, v => _entry.CoverFolder = v);
    }

    public string CoverSuffix
    {
        get => _entry.CoverSuffix;
        set => SetEntryValue(_entry.CoverSuffix, value ?? string.Empty, v => _entry.CoverSuffix = v);
    }

    /// <summary>Largeur max saisie — le texte est conservé tel quel, la valeur entière n'est mise à jour que si elle est valide.</summary>
    public string CoverMaxWidthText
    {
        get => _coverMaxWidthText;
        set
        {
            if (!SetProperty(ref _coverMaxWidthText, value ?? string.Empty)) return;
            _coverMaxWidthValid = TryParseSize(_coverMaxWidthText, out int size);
            if (_coverMaxWidthValid) _entry.CoverMaxWidth = size;
            MarkDirty();
        }
    }

    /// <summary>Hauteur max saisie — même règle que la largeur.</summary>
    public string CoverMaxHeightText
    {
        get => _coverMaxHeightText;
        set
        {
            if (!SetProperty(ref _coverMaxHeightText, value ?? string.Empty)) return;
            _coverMaxHeightValid = TryParseSize(_coverMaxHeightText, out int size);
            if (_coverMaxHeightValid) _entry.CoverMaxHeight = size;
            MarkDirty();
        }
    }

    // ── Métadonnées ───────────────────────────────────────────────────────

    /// <summary>
    /// 0 = aucun fichier de métadonnées, puis les formats de <see cref="MetadataFormats.All"/> dans leur ordre.
    /// Passer à 0 désactive aussi "gamelist par défaut".
    /// </summary>
    public int GamelistFormatIndex
    {
        get
        {
            string? known = MetadataFormats.Normalize(_entry.GamelistFormat);
            return known is null ? 0 : MetadataFormats.All.ToList().IndexOf(known) + 1;
        }
        set
        {
            string? format = value >= 1 && value <= MetadataFormats.All.Count ? MetadataFormats.All[value - 1] : null;
            if (string.Equals(_entry.GamelistFormat, format, StringComparison.OrdinalIgnoreCase)) return;
            _entry.GamelistFormat = format;
            if (format is null && _entry.GamelistByDefault)
            {
                _entry.GamelistByDefault = false;
                OnPropertyChanged(nameof(GamelistByDefault));
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(SupportsGamelist));
            OnPropertyChanged(nameof(GamelistFormatHint));
            MarkDirty();
        }
    }

    public bool SupportsGamelist => _entry.SupportsGamelist;

    /// <summary>Une ligne sous le sélecteur : quel fichier sera écrit, où, et pour quels frontends.</summary>
    public string GamelistFormatHint => MetadataFormats.Normalize(_entry.GamelistFormat) switch
    {
        MetadataFormats.EmulationStation => Strings.ArchEditor_GamelistHint_EmulationStation,
        MetadataFormats.EsDe             => Strings.ArchEditor_GamelistHint_EsDe,
        MetadataFormats.Miyoo            => Strings.ArchEditor_GamelistHint_Miyoo,
        MetadataFormats.Pegasus          => Strings.ArchEditor_GamelistHint_Pegasus,
        MetadataFormats.Logiqx           => Strings.ArchEditor_GamelistHint_Logiqx,
        _                                => Strings.ArchEditor_GamelistHint_None,
    };

    public bool GamelistByDefault
    {
        get => _entry.GamelistByDefault;
        set => SetEntryValue(_entry.GamelistByDefault, value, v => _entry.GamelistByDefault = v);
    }

    // ── Cycle de vie ──────────────────────────────────────────────────────

    /// <summary>Remplit la table des systèmes sans marquer le brouillon modifié.</summary>
    public void LoadMappings(FolderTreeMapping mapping)
    {
        _suspendDirty = true;
        try
        {
            Mappings.Clear();
            foreach (var m in mapping.FolderTreeMappings)
                Mappings.Add(new SystemMappingRowViewModel(m));
            _mappingsLoaded = true;
            OnPropertyChanged(nameof(MappingsLoaded));
            RefreshAvailableSystems();
        }
        finally
        {
            _suspendDirty = false;
        }
    }

    /// <summary>Table des systèmes prête à être sérialisée.</summary>
    public FolderTreeMapping ToMapping()
        => new() { FolderTreeMappings = Mappings.Select(r => r.ToModel()).ToList() };

    /// <summary>Force l'état modifié — pour une création de la session, à écrire même sans retouche.</summary>
    public void MarkDirty()
    {
        if (_suspendDirty) return;
        IsDirty = true;
    }

    /// <summary>Après écriture réussie : plus de modification en attente, une distribuée devient surchargée.</summary>
    public void MarkSaved()
    {
        // Une architecture par défaut modifiée reste « par défaut » : tout vit dans le dossier utilisateur,
        // et la restauration globale recopie les fichiers d'origine
        IsPersisted = true;
        IsDirty     = false;
    }

    /// <summary>Remplace tout le brouillon par une entrée et sa table — sert à restaurer la version distribuée.</summary>
    public void ReplaceWith(ArchitectureEntry entry, FolderTreeMapping mapping)
    {
        _entry              = entry.Clone();
        _coverMaxWidthText  = _entry.CoverMaxWidth.ToString(CultureInfo.InvariantCulture);
        _coverMaxHeightText = _entry.CoverMaxHeight.ToString(CultureInfo.InvariantCulture);
        _coverMaxWidthValid = _coverMaxHeightValid = true;
        LoadMappings(mapping);
        IsPersisted = true;
        IsDirty     = false;
        OnPropertyChanged(string.Empty); // toutes les liaisons se rafraîchissent
    }

    /// <summary>Nettoie les champs texte avant écriture — sans passer par les setters pour ne pas toucher IsDirty.</summary>
    public void Normalize()
    {
        _entry.Label       = _entry.Label.Trim();
        _entry.Description = _entry.Description.Trim();
        _entry.CoverFolder = _entry.CoverFolder.Trim();
        _entry.CoverSuffix = _entry.CoverSuffix.Trim();
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(CoverFolder));
        OnPropertyChanged(nameof(CoverSuffix));
    }

    /// <summary>Messages d'erreur bloquant l'enregistrement — liste vide si le brouillon est valide.</summary>
    public List<string> Validate()
    {
        var errors = new List<string>();
        string name = DisplayName;

        if (string.IsNullOrWhiteSpace(_entry.Label))
            errors.Add(string.Format(Strings.ArchEditor_Validation_EmptyLabel, name));

        if (!_coverMaxWidthValid || !_coverMaxHeightValid)
            errors.Add(string.Format(Strings.ArchEditor_Validation_InvalidSize, name));

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in Mappings)
        {
            string system = row.RomStationSystem.Trim();
            if (system.Length == 0)
            {
                errors.Add(string.Format(Strings.ArchEditor_Validation_EmptySystem, name));
                continue;
            }
            if (string.IsNullOrWhiteSpace(row.TargetFolder))
                errors.Add(string.Format(Strings.ArchEditor_Validation_EmptyFolder, name, system));
            if (!seen.Add(system))
                errors.Add(string.Format(Strings.ArchEditor_Validation_DuplicateSystem, name, system));
        }
        return errors;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>Écrit une valeur dans l'entrée, notifie et marque le brouillon modifié si elle change.</summary>
    private void SetEntryValue<T>(T current, T value, Action<T> assign, string? alsoNotify = null,
                                  [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(current, value)) return;
        assign(value);
        OnPropertyChanged(propertyName);
        if (alsoNotify is not null) OnPropertyChanged(alsoNotify);
        MarkDirty();
    }

    /// <summary>Entier positif ou nul ; le champ vide vaut 0 (taille d'origine).</summary>
    private static bool TryParseSize(string text, out int size)
    {
        string trimmed = text.Trim();
        if (trimmed.Length == 0) { size = 0; return true; }
        return int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out size) && size >= 0;
    }

    private void OnMappingsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var row in _trackedRows) row.PropertyChanged -= OnRowChanged;
            _trackedRows.Clear();
        }
        if (e.OldItems is not null)
            foreach (SystemMappingRowViewModel row in e.OldItems)
            {
                row.PropertyChanged -= OnRowChanged;
                _trackedRows.Remove(row);
            }
        if (e.NewItems is not null)
            foreach (SystemMappingRowViewModel row in e.NewItems)
            {
                row.PropertyChanged += OnRowChanged;
                _trackedRows.Add(row);
            }
        RefreshAvailableSystems();
        MarkDirty();
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        // AvailableSystems est posé par ce brouillon lui-même, IconPath n'est qu'un affichage : ni sale, ni recalcul
        if (e.PropertyName is nameof(SystemMappingRowViewModel.AvailableSystems) or nameof(SystemMappingRowViewModel.IconPath)) return;
        if (e.PropertyName == nameof(SystemMappingRowViewModel.RomStationSystem))
            RefreshAvailableSystems();
        MarkDirty();
    }
}
