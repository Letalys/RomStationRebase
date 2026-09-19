using RomStationRebase.Models;
using RomStationRebase.Resources;

namespace RomStationRebase.ViewModels;

/// <summary>Représente un jeu dans la liste de progression de RebaseWindow.</summary>
public class RebaseGameItemViewModel : ViewModelBase
{
    private RebaseItemStatus _status    = RebaseItemStatus.Pending;
    private double           _progress;
    private string?          _errorDetail;
    private RebaseGamePlan?  _plan;

    // Règles du système dans l'architecture cible, et surcharges posées pour ce jeu seulement
    private bool  _archKeepFileName, _archM3U, _archExtract, _isMapped;
    private bool? _keepFileNameOverride, _m3uOverride, _extractOverride;
    private ArchiveMode _archiveMode = ArchiveMode.ExtractRequired;

    // Conversion : outil que l'architecture désigne pour ce système ("" = aucun), choix posé pour ce jeu (null = architecture)
    private string  _archTransform = string.Empty;
    private string? _transformOverride;
    private bool    _conversionEnabled;
    private IReadOnlyList<ToolOption> _toolOptions = [ToolOption.None];

    // ── Données fixes ─────────────────────────────────────────────────────

    /// <summary>Identifiant Derby du jeu.</summary>
    public int     GameId          { get; init; }

    /// <summary>Titre du jeu.</summary>
    public string  Title           { get; init; } = string.Empty;

    /// <summary>Nom du système auquel appartient ce jeu.</summary>
    public string  SystemName      { get; init; } = string.Empty;

    /// <summary>Chemin de l'image du système. Null si absent.</summary>
    public string? SystemImagePath { get; init; }

    /// <summary>Nombre de fichiers ROM à copier.</summary>
    public int     FileCount       { get; init; }

    /// <summary>Chemin de la jaquette. Null si absent — le placeholder gris s'affiche à la place.</summary>
    public string? CoverPath       { get; init; }

    /// <summary>True si la jaquette existe physiquement sur le disque.</summary>
    public bool    CoverExists     { get; init; }

    // ── Règles par système et surcharges par jeu ──────────────────────────

    /// <summary>Appelé quand l'utilisateur bascule une règle de la ligne — le ViewModel parent recalcule le plan.</summary>
    public Action? RuleChanged { get; set; }

    // Principe d'affichage : un interrupteur montre ce qui arrivera réellement à CE jeu. Quand une règle n'a pas
    // d'objet pour lui (M3U d'un jeu à un seul disque, extraction d'un romset), il est grisé ET éteint, jamais
    // « allumé mais grisé » : la valeur de l'architecture n'est montrée que là où elle s'applique.

    /// <summary>Pose les valeurs de l'architecture pour le système de ce jeu (appelé après chaque replanification).</summary>
    public void SetArchitectureRules(bool isMapped, bool keepFileName, bool m3u, bool extract,
                                     ArchiveMode archiveMode = ArchiveMode.ExtractRequired)
    {
        _isMapped         = isMapped;
        _archKeepFileName = keepFileName;
        _archM3U          = m3u;
        _archExtract      = extract;
        _archiveMode      = archiveMode;
        NotifyRules();
    }

    /// <summary>
    /// Conversion : l'outil que l'architecture désigne pour le système de cette ligne ("" = aucun, ou indisponible),
    /// les outils utilisables pour ce jeu (« Aucune » en tête), et l'intention du passage (interrupteur de la fenêtre).
    /// </summary>
    public void SetConversion(string archTransform, IReadOnlyList<ToolOption> options, bool enabled)
    {
        _archTransform     = archTransform;
        _conversionEnabled = enabled;
        // La liste n'est remplacée que si elle change : une ComboBox perd sa sélection quand on lui change sa liste
        if (!_toolOptions.SequenceEqual(options))
        {
            _toolOptions = options;
            OnPropertyChanged(nameof(ToolOptions));
        }
        NotifyRules();
    }

    private void NotifyRules()
    {
        OnPropertyChanged(nameof(IsMapped));
        OnPropertyChanged(nameof(KeepFileName));
        OnPropertyChanged(nameof(M3U));
        OnPropertyChanged(nameof(Extract));
        OnPropertyChanged(nameof(CanToggleM3U));
        OnPropertyChanged(nameof(CanToggleExtract));
        OnPropertyChanged(nameof(M3UTooltip));
        OnPropertyChanged(nameof(ExtractTooltip));
        OnPropertyChanged(nameof(Transform));
        OnPropertyChanged(nameof(CanChooseTransform));
        OnPropertyChanged(nameof(TransformTooltip));
    }

    /// <summary>True si le système du jeu a une correspondance dans l'architecture — sinon les règles n'ont pas d'objet.</summary>
    public bool IsMapped => _isMapped;

    /// <summary>Romset : le fichier garde son nom d'origine, n'est jamais extrait, jamais converti et n'a jamais de M3U.</summary>
    public bool KeepFileName
    {
        get => _isMapped && (_keepFileNameOverride ?? _archKeepFileName);
        set => SetOverride(ref _keepFileNameOverride, value, _archKeepFileName);
    }

    /// <summary>Une playlist M3U sera écrite pour ce jeu.</summary>
    public bool M3U
    {
        get => CanToggleM3U && (_m3uOverride ?? _archM3U);
        set => SetOverride(ref _m3uOverride, value, _archM3U);
    }

    /// <summary>L'archive de ce jeu sera extraite (sur la cible, ou dans le dossier de travail d'une conversion).</summary>
    public bool Extract
    {
        get => IsConverted            ? HasArchive
             : CanToggleExtract       ? _extractOverride ?? _archExtract
             : _isMapped && !KeepFileName && HasArchive && _archiveMode == ArchiveMode.ExtractAll;
        set => SetOverride(ref _extractOverride, value, _archExtract);
    }

    /// <summary>Le M3U n'a d'objet que pour de vrais disques (pas des versions d'un même jeu), hors romset.</summary>
    public bool CanToggleM3U => _isMapped && !KeepFileName && (_plan?.IsDiscSet ?? FileCount > 1);

    /// <summary>
    /// L'extraction se choisit jeu par jeu seulement en mode « selon l'architecture », pour une archive, hors romset,
    /// et tant qu'aucune conversion ne s'en charge elle-même.
    /// </summary>
    public bool CanToggleExtract => _isMapped && !KeepFileName && HasArchive && !IsConverted
                                    && _archiveMode == ArchiveMode.ExtractRequired;

    private bool HasArchive  => _plan?.HasArchive ?? true;
    private bool IsConverted => _plan is not null && _plan.Files.Any(f => f.Kind == FileTransferKind.Transform);

    public string M3UTooltip => CanToggleM3U ? Strings.Rebase_Column_M3U_Tooltip
        : KeepFileName ? Strings.Rebase_Column_Off_Romset
        :                Strings.Rebase_Column_M3U_Off_SingleDisc;

    public string ExtractTooltip => IsConverted ? Strings.Rebase_Column_Extract_Converted
        : KeepFileName                            ? Strings.Rebase_Column_Off_Romset
        : !HasArchive                             ? Strings.Rebase_Column_Extract_Off_NoArchive
        : _archiveMode != ArchiveMode.ExtractRequired ? Strings.Rebase_Column_Extract_Off_Mode
        :                                           Strings.Rebase_Column_Extract_Tooltip;

    // ── Conversion ────────────────────────────────────────────────────────

    /// <summary>Outils utilisables pour ce jeu, « Aucune » en tête.</summary>
    public IReadOnlyList<ToolOption> ToolOptions => _toolOptions;

    /// <summary>
    /// Identifiant de l'outil qui convertira ce jeu, "" pour aucun. Valeur effective : un outil que ce jeu ne peut pas
    /// utiliser (romset, contenu que l'outil ne lit pas, exécutable manquant) retombe sur « Aucune ».
    /// </summary>
    public string Transform
    {
        get
        {
            if (!CanChooseTransform) return string.Empty;
            string wanted = _transformOverride ?? _archTransform;
            return _toolOptions.Any(o => string.Equals(o.Id, wanted, StringComparison.OrdinalIgnoreCase)) ? wanted : string.Empty;
        }
        set
        {
            // Une ComboBox dont on remplace la liste écrit null : ce n'est pas un choix de l'utilisateur
            if (value is null || !CanChooseTransform) return;
            if (string.Equals(Transform, value, StringComparison.OrdinalIgnoreCase)) return;
            _transformOverride = string.Equals(value, _archTransform, StringComparison.OrdinalIgnoreCase) ? null : value;
            OnPropertyChanged();
            RuleChanged?.Invoke();
        }
    }

    /// <summary>
    /// La liste reste ouverte même quand aucun outil ne convient : elle ne propose alors que « Aucune » (demande du dev).
    /// Elle n'est grisée que pour un romset, ou quand les conversions sont désactivées pour ce passage.
    /// </summary>
    public bool CanChooseTransform => _isMapped && _conversionEnabled && !KeepFileName;

    public string TransformTooltip => KeepFileName ? Strings.Rebase_Column_Off_Romset
        : !_conversionEnabled   ? Strings.Rebase_Column_Convert_Off_Switch
        : _toolOptions.Count > 1 ? Strings.Rebase_Column_Convert_Tooltip
        : _plan is { ToolInputExtensions.Count: > 0 }
                                ? string.Format(Strings.Rebase_Column_Convert_Off_NoToolFor, string.Join(", ", _plan.ToolInputExtensions))
        :                         Strings.Rebase_Column_Convert_Off_NoTool;

    /// <summary>
    /// Repose les surcharges lues dans une présélection, avant le premier plan : ni notification
    /// ni replanification, et une surcharge égale à l'architecture sera effacée au premier basculement.
    /// </summary>
    public void RestoreOverrides(bool? keepFileName, bool? m3u, bool? extract, string? transform = null)
    {
        _keepFileNameOverride = keepFileName;
        _m3uOverride          = m3u;
        _extractOverride      = extract;
        _transformOverride    = transform;
    }

    /// <summary>Surcharges de ce jeu, telles que transmises au planificateur (null = valeur de l'architecture).</summary>
    public bool?   KeepFileNameOverride => _keepFileNameOverride;
    public bool?   M3UOverride          => _m3uOverride;
    public bool?   ExtractOverride      => _extractOverride;
    public string? TransformOverride    => _transformOverride;

    /// <summary>Une surcharge égale à la valeur de l'architecture est effacée : la ligne redit alors l'architecture.</summary>
    private void SetOverride(ref bool? field, bool value, bool archValue)
    {
        bool? newValue = value == archValue ? null : value;
        if ((field ?? archValue) == value) { field = newValue; return; }
        field = newValue;
        NotifyRules();
        RuleChanged?.Invoke();
    }

    // ── Plan ──────────────────────────────────────────────────────────────

    /// <summary>Plan du jeu, posé une fois le plan calculé — pilote la colonne "Sortie" et son infobulle.</summary>
    public RebaseGamePlan? Plan
    {
        get => _plan;
        set
        {
            if (SetProperty(ref _plan, value))
            {
                OnPropertyChanged(nameof(OutputText));
                OnPropertyChanged(nameof(OutputTooltip));
                OnPropertyChanged(nameof(IsUnmapped));
                NotifyRules();
            }
        }
    }

    /// <summary>True si le système du jeu n'a pas de correspondance dans l'architecture choisie.</summary>
    public bool IsUnmapped => _plan?.IsUnmapped == true;

    /// <summary>Résumé localisé de ce qui sera produit pour ce jeu.</summary>
    public string OutputText => _plan is null ? string.Empty : _plan.OutputKind switch
    {
        GameOutputKind.Romset   => Strings.Rebase_Output_Romset,
        GameOutputKind.DiscSet when IsConverted
                                => string.Format(Strings.Rebase_Output_DiscSet_Converted, _plan.Files.Count, ConvertedExtension),
        GameOutputKind.DiscSet  => string.Format(Strings.Rebase_Output_DiscSet, _plan.Files.Count),
        GameOutputKind.Discs    => string.Format(Strings.Rebase_Output_Discs, _plan.Files.Count),
        GameOutputKind.Variants => string.Format(Strings.Rebase_Output_Variants, _plan.Files.Count),
        GameOutputKind.Extract  => Strings.Rebase_Output_Extract,
        GameOutputKind.Converted => string.Format(Strings.Rebase_Output_Converted, ConvertedExtension),
        GameOutputKind.Folder   => Strings.Rebase_Output_Folder,
        GameOutputKind.Unmapped => Strings.Rebase_Output_Unmapped,
        _                       => Strings.Rebase_Output_Copy,
    };

    /// <summary>Extension produite par la conversion (".chd"), lue dans le plan.</summary>
    private string ConvertedExtension
        => System.IO.Path.GetExtension(_plan?.Files.FirstOrDefault(f => f.Kind == FileTransferKind.Transform)?.LaunchRelativePath ?? string.Empty);

    /// <summary>Liste des chemins qui seront écrits, pour l'infobulle de la colonne "Sortie".</summary>
    public string OutputTooltip => _plan is null || _plan.OutputPaths.Count == 0
        ? OutputText
        : Strings.Rebase_Output_Tooltip + Environment.NewLine + string.Join(Environment.NewLine, _plan.OutputPaths);

    // ── Propriétés de progression ─────────────────────────────────────────

    /// <summary>Statut courant du jeu dans le pipeline de rebase.</summary>
    public RebaseItemStatus Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusIcon));
            }
        }
    }

    /// <summary>Progression de la copie en pourcentage (0–100). Non utilisé pour le statut final.</summary>
    public double Progress
    {
        get => _progress;
        set => SetProperty(ref _progress, value);
    }

    /// <summary>Message d'erreur si le statut est Failed, ou avertissement non bloquant. Null sinon.</summary>
    public string? ErrorDetail
    {
        get => _errorDetail;
        set => SetProperty(ref _errorDetail, value);
    }

    // ── Propriétés calculées ──────────────────────────────────────────────

    /// <summary>Libellé localisé du statut courant.</summary>
    public string StatusText => Status switch
    {
        RebaseItemStatus.Pending    => Strings.Status_Pending,
        RebaseItemStatus.Copying    => Strings.Rebase_Status_Copying,
        RebaseItemStatus.Extracting => Strings.Rebase_Status_Extracting,
        RebaseItemStatus.Converting => Strings.Rebase_Status_Converting,
        RebaseItemStatus.Done       => Strings.Rebase_Status_Done,
        RebaseItemStatus.Skipped    => Strings.Rebase_Status_Skipped,
        RebaseItemStatus.Failed     => Strings.Rebase_Status_Failed,
        _                           => string.Empty,
    };

    /// <summary>Icône Unicode représentant le statut courant — affichée dans la colonne icône du DataGrid.</summary>
    public string StatusIcon => Status switch
    {
        RebaseItemStatus.Pending    => "○",
        RebaseItemStatus.Copying    => "⟳",
        RebaseItemStatus.Extracting => "⤓",
        RebaseItemStatus.Converting => "⇄",
        RebaseItemStatus.Done       => "✓",
        RebaseItemStatus.Skipped    => "—",
        RebaseItemStatus.Failed     => "✗",
        _                           => "",
    };
}

/// <summary>Entrée de la liste Conversion d'une ligne du rebase : identifiant de l'outil ("" = aucune) et libellé.</summary>
public sealed record ToolOption(string Id, string Label)
{
    public static ToolOption None { get; } = new(string.Empty, Strings.ArchEditor_Convert_None);

    /// <summary>Nom lu par les lecteurs d'écran : le libellé, pas la forme technique de l'enregistrement.</summary>
    public override string ToString() => Label;
}
