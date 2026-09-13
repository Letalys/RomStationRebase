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

    /// <summary>Pose les valeurs de l'architecture pour le système de ce jeu (appelé après chaque replanification).</summary>
    public void SetArchitectureRules(bool isMapped, bool keepFileName, bool m3u, bool extract)
    {
        _isMapped         = isMapped;
        _archKeepFileName = keepFileName;
        _archM3U          = m3u;
        _archExtract      = extract;
        OnPropertyChanged(nameof(IsMapped));
        OnPropertyChanged(nameof(KeepFileName));
        OnPropertyChanged(nameof(M3U));
        OnPropertyChanged(nameof(Extract));
        OnPropertyChanged(nameof(CanToggleM3U));
        OnPropertyChanged(nameof(CanToggleExtract));
    }

    /// <summary>True si le système du jeu a une correspondance dans l'architecture — sinon les règles n'ont pas d'objet.</summary>
    public bool IsMapped => _isMapped;

    /// <summary>Romset : le fichier garde son nom d'origine, n'est jamais extrait et n'a jamais de M3U.</summary>
    public bool KeepFileName
    {
        get => _keepFileNameOverride ?? _archKeepFileName;
        set => SetOverride(ref _keepFileNameOverride, value, _archKeepFileName,
                           nameof(KeepFileName), nameof(CanToggleM3U), nameof(CanToggleExtract));
    }

    /// <summary>L'émulateur lit une playlist M3U pour ce système.</summary>
    public bool M3U
    {
        get => _m3uOverride ?? _archM3U;
        set => SetOverride(ref _m3uOverride, value, _archM3U, nameof(M3U));
    }

    /// <summary>L'émulateur exige l'extraction de l'archive pour ce système.</summary>
    public bool Extract
    {
        get => _extractOverride ?? _archExtract;
        set => SetOverride(ref _extractOverride, value, _archExtract, nameof(Extract));
    }

    /// <summary>Le M3U n'a de sens que pour un jeu à plusieurs fichiers qui n'est pas un romset.</summary>
    public bool CanToggleM3U => _isMapped && FileCount > 1 && !KeepFileName;

    /// <summary>L'extraction n'a de sens que hors romset ; le mode Archives de la fenêtre doit être « selon l'architecture ».</summary>
    public bool CanToggleExtract => _isMapped && !KeepFileName;

    /// <summary>Surcharges de ce jeu, telles que transmises au planificateur (null = valeur de l'architecture).</summary>
    public bool? KeepFileNameOverride => _keepFileNameOverride;
    public bool? M3UOverride          => _m3uOverride;
    public bool? ExtractOverride      => _extractOverride;

    /// <summary>Une surcharge égale à la valeur de l'architecture est effacée : la ligne redit alors l'architecture.</summary>
    private void SetOverride(ref bool? field, bool value, bool archValue, params string[] changed)
    {
        bool? newValue = value == archValue ? null : value;
        if ((field ?? archValue) == value) { field = newValue; return; }
        field = newValue;
        foreach (var name in changed) OnPropertyChanged(name);
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
            }
        }
    }

    /// <summary>True si le système du jeu n'a pas de correspondance dans l'architecture choisie.</summary>
    public bool IsUnmapped => _plan?.IsUnmapped == true;

    /// <summary>Résumé localisé de ce qui sera produit pour ce jeu.</summary>
    public string OutputText => _plan is null ? string.Empty : _plan.OutputKind switch
    {
        GameOutputKind.Romset   => Strings.Rebase_Output_Romset,
        GameOutputKind.DiscSet  => string.Format(Strings.Rebase_Output_DiscSet, _plan.Files.Count),
        GameOutputKind.Discs    => string.Format(Strings.Rebase_Output_Discs, _plan.Files.Count),
        GameOutputKind.Variants => string.Format(Strings.Rebase_Output_Variants, _plan.Files.Count),
        GameOutputKind.Extract  => Strings.Rebase_Output_Extract,
        GameOutputKind.Folder   => Strings.Rebase_Output_Folder,
        GameOutputKind.Unmapped => Strings.Rebase_Output_Unmapped,
        _                       => Strings.Rebase_Output_Copy,
    };

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
        RebaseItemStatus.Done       => "✓",
        RebaseItemStatus.Skipped    => "—",
        RebaseItemStatus.Failed     => "✗",
        _                           => "",
    };
}
