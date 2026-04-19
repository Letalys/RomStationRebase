using RomStationRebase.Models;
using RomStationRebase.Resources;

namespace RomStationRebase.ViewModels;

/// <summary>Représente un jeu dans la liste de progression de RebaseWindow.</summary>
public class RebaseGameItemViewModel : ViewModelBase
{
    private RebaseItemStatus _status    = RebaseItemStatus.Pending;
    private double           _progress;
    private string?          _errorDetail;

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

    /// <summary>Message d'erreur si le statut est Failed. Null sinon.</summary>
    public string? ErrorDetail
    {
        get => _errorDetail;
        set => SetProperty(ref _errorDetail, value);
    }

    // ── Propriétés calculées ──────────────────────────────────────────────

    /// <summary>Libellé localisé du statut courant.</summary>
    public string StatusText => Status switch
    {
        RebaseItemStatus.Pending  => Strings.Status_Pending,
        RebaseItemStatus.Copying  => Strings.Rebase_Status_Copying,
        RebaseItemStatus.Done     => Strings.Rebase_Status_Done,
        RebaseItemStatus.Skipped  => Strings.Rebase_Status_Skipped,
        RebaseItemStatus.Failed   => Strings.Rebase_Status_Failed,
        _                         => string.Empty,
    };

    /// <summary>Icône Unicode représentant le statut courant — affichée dans la colonne icône du DataGrid.</summary>
    public string StatusIcon => Status switch
    {
        RebaseItemStatus.Pending  => "○",
        RebaseItemStatus.Copying  => "⟳",
        RebaseItemStatus.Done     => "✓",
        RebaseItemStatus.Skipped  => "—",
        RebaseItemStatus.Failed   => "✗",
        _                         => "",
    };
}
