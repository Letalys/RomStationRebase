using System.Windows.Input;
using RomStationRebase.Resources;

namespace RomStationRebase.ViewModels;

/// <summary>Représente un jeu dans la grille principale, avec son état de sélection et ses indicateurs de problèmes.</summary>
public class GameItemViewModel : ViewModelBase
{
    private readonly Action _onSelectionChanged;
    private bool _isSelected;

    // ── Données du jeu ────────────────────────────────────────────────────

    /// <summary>Identifiant Derby du jeu.</summary>
    public int Id { get; }

    /// <summary>Titre du jeu.</summary>
    public string Title { get; }

    /// <summary>Nom du système auquel appartient ce jeu.</summary>
    public string SystemName { get; }

    /// <summary>Chemin vers l'image du système. Null si absent.</summary>
    public string? SystemImagePath { get; }

    /// <summary>Chemin calculé vers la jaquette. Null si non disponible.</summary>
    public string? CoverPath { get; }

    /// <summary>True si le fichier de jaquette existe sur le disque.</summary>
    public bool CoverExists { get; }

    /// <summary>True si au moins un fichier ROM existe sur le disque.</summary>
    public bool FileExists { get; }

    /// <summary>Nombre de fichiers ROM associés (multi-disques, etc.).</summary>
    public int FileCount { get; }

    /// <summary>
    /// Chemin Derby vers un fichier du jeu (ex: Games/Downloads/psx/Titre/disc1/rom.zip).
    /// Sert à localiser le dossier racine du jeu pour la copie lors du rebase.
    /// </summary>
    public string GameDirectory { get; }

    /// <summary>Indique si ce jeu a déjà été exporté lors d'un rebase précédent.</summary>
    public bool IsExported { get; }

    // ── Propriétés calculées ──────────────────────────────────────────────

    /// <summary>True si le jeu présente un problème (jaquette ou fichier manquant).</summary>
    public bool HasIssues => !CoverExists || !FileExists;

    /// <summary>True si le jeu a plusieurs fichiers ROM (multi-disques).</summary>
    public bool HasMultipleFiles => FileCount > 1;

    /// <summary>Texte localisé affichant le nombre de fichiers — ex. "2 fichiers".</summary>
    public string FileCountText => string.Format(Strings.Badge_MultiDisc, FileCount);

    // ── État de sélection ─────────────────────────────────────────────────

    /// <summary>
    /// Indique si le jeu est coché pour le prochain rebase.
    /// Tout changement déclenche le recalcul de SelectedGameCount dans MainViewModel.
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set { if (SetProperty(ref _isSelected, value)) _onSelectionChanged(); }
    }

    /// <summary>Inverse IsSelected et notifie le MainViewModel via le callback.</summary>
    public ICommand ToggleSelectCommand { get; }

    // ── Constructeur ──────────────────────────────────────────────────────

    /// <param name="id">Identifiant Derby.</param>
    /// <param name="title">Titre du jeu.</param>
    /// <param name="systemName">Nom du système.</param>
    /// <param name="systemImagePath">Chemin de l'image système.</param>
    /// <param name="coverPath">Chemin de la jaquette.</param>
    /// <param name="coverExists">La jaquette existe physiquement.</param>
    /// <param name="fileExists">Au moins un fichier ROM existe.</param>
    /// <param name="fileCount">Nombre de fichiers ROM.</param>
    /// <param name="gameDirectory">Chemin Derby vers un fichier du jeu (pour résoudre le dossier racine).</param>
    /// <param name="isSelected">Sélectionné au départ.</param>
    /// <param name="isExported">Déjà exporté.</param>
    /// <param name="onSelectionChanged">Callback appelé quand IsSelected change.</param>
    public GameItemViewModel(
        int id, string title, string systemName, string? systemImagePath,
        string? coverPath, bool coverExists, bool fileExists,
        int fileCount, string gameDirectory, bool isSelected, bool isExported,
        Action onSelectionChanged)
    {
        Id              = id;
        Title           = title;
        SystemName      = systemName;
        SystemImagePath = systemImagePath;
        CoverPath       = coverPath;
        CoverExists     = coverExists;
        FileExists      = fileExists;
        FileCount       = fileCount;
        GameDirectory   = gameDirectory;
        _isSelected     = isSelected;
        IsExported      = isExported;
        _onSelectionChanged = onSelectionChanged;

        ToggleSelectCommand = new RelayCommand(() => IsSelected = !IsSelected);
    }
}
