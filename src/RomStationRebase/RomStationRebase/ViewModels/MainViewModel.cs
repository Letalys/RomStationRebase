using RomStationRebase.Helpers;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using RomStationRebase.Models;
using RomStationRebase.Resources;
using RomStationRebase.Services;
using RomStationRebase.Views.Dialogs;

namespace RomStationRebase.ViewModels;

/// <summary>ViewModel principal — expose les systèmes, les jeux filtrés et l'état global de l'interface.</summary>
public class MainViewModel : ViewModelBase
{
    private readonly DerbyService        _derby       = new();
    private readonly RomStationService   _rs          = new();
    private readonly ConfigService       _config      = new();
    private readonly UserPreferences     _preferences;
    private          AppMetadata         _metadata    = new();

    private bool   _isUpdateAvailable;
    private string _updateAvailableText = string.Empty;

    private string    _romStationPath    = string.Empty;
    private string    _dbCopyPath        = string.Empty;
    private string    _searchText        = string.Empty;
    private bool      _isMosaicView      = true;
    private bool      _showIssuesOnly;
    private bool      _hideEmptySystems;
    private string    _thumbnailSize     = "Normal";
    private bool      _isLoading         = true;   // true par défaut — overlay visible jusqu'à la fin de LoadLibraryAsync
    private int       _selectedGameCount;
    private DateTime? _lastSyncDate;
    private string    _loadingStatusText = string.Empty;
    private double    _loadingProgress;
    private string    _sortColumn        = "Title";
    private bool      _sortAscending     = true;

    // ── Collections ───────────────────────────────────────────────────────

    /// <summary>Liste complète de tous les systèmes — source de vérité, non filtrée.</summary>
    public ObservableCollection<SystemItemViewModel> Systems { get; } = new();

    /// <summary>Systèmes visibles dans la sidebar — filtrés selon HideEmptySystems.</summary>
    private ObservableCollection<SystemItemViewModel> _visibleSystems = new();
    public ObservableCollection<SystemItemViewModel> VisibleSystems
    {
        get => _visibleSystems;
        private set
        {
            _visibleSystems = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Liste complète des jeux (source de vérité, non filtrée).</summary>
    public ObservableCollection<GameItemViewModel> Games { get; } = new();

    /// <summary>Jeux visibles après application des filtres système, recherche et problèmes.</summary>
    private ObservableCollection<GameItemViewModel> _filteredGames = new();
    public ObservableCollection<GameItemViewModel> FilteredGames
    {
        get => _filteredGames;
        private set
        {
            _filteredGames = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FilteredGameCount));
        }
    }

    /// <summary>Items de l'abécédaire — 26 lettres A-Z + #, état activé selon FilteredGames et SortColumn.</summary>
    public ObservableCollection<AlphaItemViewModel> AlphabetItems { get; private set; } = null!;

    /// <summary>Callback injecté par MainWindow.xaml.cs — effectue le scroll vers la première occurrence de la lettre.</summary>
    public Action<string>? ScrollToLetter { get; set; }

    // ── Propriétés bindées ────────────────────────────────────────────────

    /// <summary>Texte saisi dans la barre de recherche — déclenche le filtrage à chaque frappe.</summary>
    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value)) RefreshFilter(); }
    }

    /// <summary>Nombre de jeux actuellement cochés pour le rebase.</summary>
    public int SelectedGameCount
    {
        get => _selectedGameCount;
        private set
        {
            if (SetProperty(ref _selectedGameCount, value))
            {
                OnPropertyChanged(nameof(SelectedCountText));
                OnPropertyChanged(nameof(HasSelection));
            }
        }
    }

    /// <summary>True dès qu'un jeu est coché, filtres compris — pilote le badge de sélection du header.</summary>
    public bool HasSelection => _selectedGameCount > 0;

    private bool _hasNoArchitecture;

    /// <summary>True si aucune architecture cible n'est disponible (toutes supprimées) : le rebase est impossible, la barre de statut l'affiche.</summary>
    public bool HasNoArchitecture
    {
        get => _hasNoArchitecture;
        private set => SetProperty(ref _hasNoArchitecture, value);
    }

    /// <summary>Relit l'index des architectures — à l'ouverture, puis après chaque fenêtre qui peut les modifier.</summary>
    private void RefreshArchitectureWarning()
    {
        try   { HasNoArchitecture = new ArchitectureService().LoadArchitectures().Count == 0; }
        catch { HasNoArchitecture = true; }
    }

    /// <summary>Texte formaté de la statusbar — utilise la chaîne localisée Strings.SelectedCount.</summary>
    public string SelectedCountText
        => string.Format(Strings.SelectedCount, _selectedGameCount);

    /// <summary>Nombre de jeux visibles après filtrage — affiché dans le compteur du header.</summary>
    public int FilteredGameCount => FilteredGames.Count;

    /// <summary>Mode d'affichage : true = mosaïque, false = liste.</summary>
    public bool IsMosaicView
    {
        get => _isMosaicView;
        set
        {
            if (SetProperty(ref _isMosaicView, value))
            {
                OnPropertyChanged(nameof(IsListView));
                // Sauvegarde silencieuse de la préférence — ne bloque pas l'UI si échec
                SaveViewModePreference();
            }
        }
    }

    /// <summary>Inverse de IsMosaicView — utilisé pour la visibilité de la vue liste.</summary>
    public bool IsListView => !_isMosaicView;

    /// <summary>Taille des vignettes : "Normal" (défaut) ou "Grand". Déclenche le recalcul des dimensions bindées.</summary>
    public string ThumbnailSize
    {
        get => _thumbnailSize;
        set
        {
            if (SetProperty(ref _thumbnailSize, value))
            {
                OnPropertyChanged(nameof(MosaicCardWidth));
                OnPropertyChanged(nameof(MosaicCardHeight));
                OnPropertyChanged(nameof(MosaicItemWidth));
                OnPropertyChanged(nameof(MosaicItemHeight));
                OnPropertyChanged(nameof(ListThumbWidth));
                OnPropertyChanged(nameof(ListThumbHeight));
                OnPropertyChanged(nameof(ListColumnWidth));
                OnPropertyChanged(nameof(ListRowHeight));
                SaveThumbnailSizePreference();
            }
        }
    }

    // ── Dimensions calculées selon ThumbnailSize ──────────────────────────

    /// <summary>Largeur de la jaquette en mode mosaïque.</summary>
    public double MosaicCardWidth  => _thumbnailSize == "Grand" ? 288 : 184;

    /// <summary>Hauteur de la jaquette en mode mosaïque.</summary>
    public double MosaicCardHeight => _thumbnailSize == "Grand" ? 344 : 220;

    /// <summary>Largeur du conteneur item (jaquette + marges) pour VirtualizingWrapPanel.</summary>
    public double MosaicItemWidth  => MosaicCardWidth  + 32;

    /// <summary>Hauteur du conteneur item (jaquette + zone info + marges) pour VirtualizingWrapPanel.</summary>
    public double MosaicItemHeight => MosaicCardHeight + 75;

    /// <summary>Largeur/hauteur de la miniature en mode liste.</summary>
    public double ListThumbWidth   => _thumbnailSize == "Grand" ? 96 : 40;

    /// <summary>Hauteur de la miniature en mode liste (carré).</summary>
    public double ListThumbHeight  => ListThumbWidth;

    /// <summary>Largeur de la colonne miniature en mode liste (miniature + marges).</summary>
    public double ListColumnWidth  => ListThumbWidth + 12;

    /// <summary>Hauteur de ligne en mode liste — s'adapte à la miniature.</summary>
    public double ListRowHeight    => _thumbnailSize == "Grand" ? 108 : 44;

    /// <summary>Colonne de tri active — "Title", "System" ou "Files". Déclenche le retri via ApplySort().</summary>
    public string SortColumn
    {
        get => _sortColumn;
        set
        {
            if (SetProperty(ref _sortColumn, value))
            {
                OnPropertyChanged(nameof(TitleSortArrow));
                OnPropertyChanged(nameof(SystemSortArrow));
                OnPropertyChanged(nameof(FilesSortArrow));
                // Notifie SortCriteria — permet à la ComboBox toolbar de refléter un changement
                // déclenché par un clic sur un en-tête de colonne (synchro bidirectionnelle).
                OnPropertyChanged(nameof(SortCriteria));
                // Recalcul de l'abécédaire — le changement de colonne de tri peut activer ou
                // désactiver l'ensemble des lettres (abécédaire inopérant hors tri alphabétique).
                UpdateAlphabetStates();
            }
        }
    }

    /// <summary>Sens du tri — true = ascendant. Déclenche le retri via ApplySort().</summary>
    public bool SortAscending
    {
        get => _sortAscending;
        set
        {
            if (SetProperty(ref _sortAscending, value))
            {
                OnPropertyChanged(nameof(TitleSortArrow));
                OnPropertyChanged(nameof(SystemSortArrow));
                OnPropertyChanged(nameof(FilesSortArrow));
            }
        }
    }

    /// <summary>Flèche de tri bindée dans l'en-tête de la colonne Titre — "↑", "↓" ou "".</summary>
    public string TitleSortArrow  => _sortColumn == "Title"  ? (_sortAscending ? "↑" : "↓") : "";

    /// <summary>Flèche de tri bindée dans l'en-tête de la colonne Système.</summary>
    public string SystemSortArrow => _sortColumn == "System" ? (_sortAscending ? "↑" : "↓") : "";

    /// <summary>Flèche de tri bindée dans l'en-tête de la colonne Fichiers.</summary>
    public string FilesSortArrow  => _sortColumn == "Files"  ? (_sortAscending ? "↑" : "↓") : "";

    /// <summary>
    /// Point de liaison pour la ComboBox "Trier par" de la toolbar.
    /// Lecture : expose SortColumn pour que la ComboBox reflète l'état courant (y compris
    /// les changements via les en-têtes de colonnes cliquables).
    /// Écriture : appelle SetSortCriteria — force le sens ascendant et persiste la préférence.
    /// </summary>
    public string SortCriteria
    {
        get => _sortColumn;
        set
        {
            if (_sortColumn != value)
                SetSortCriteria(value);
        }
    }

    /// <summary>Quand true, seuls les jeux présentant un problème (fichier ou jaquette manquants) sont affichés.</summary>
    public bool ShowIssuesOnly
    {
        get => _showIssuesOnly;
        set
        {
            if (SetProperty(ref _showIssuesOnly, value))
            {
                RefreshFilter();
                SaveShowIssuesOnlyPreference();
            }
        }
    }

    /// <summary>Quand true, masque les consoles sans jeu dans la sidebar.</summary>
    public bool HideEmptySystems
    {
        get => _hideEmptySystems;
        set
        {
            if (SetProperty(ref _hideEmptySystems, value))
            {
                RefreshVisibleSystems();
                SaveHideEmptySystemsPreference();
            }
        }
    }

    /// <summary>True si au moins un jeu de la bibliothèque présente un problème (fichier ou jaquette manquants).</summary>
    public bool HasAnyIssues => Games.Any(g => g.HasIssues);

    /// <summary>True pendant LoadLibraryAsync — affiche l'overlay de chargement dans la vue.</summary>
    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    /// <summary>Date de la dernière synchronisation Derby, formatée pour la statusbar.</summary>
    public string LastSyncText
        => _lastSyncDate?.ToString("dd/MM/yyyy HH:mm") ?? "—";

    /// <summary>Version de l'application lue depuis l'assembly — affichée dans la titlebar.</summary>
    public string AppVersion { get; } =
        "v" + (Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.1.0");

    /// <summary>True si une mise à jour est disponible — pilote la visibilité du bouton MAJ dans la statusbar.</summary>
    public bool IsUpdateAvailable
    {
        get => _isUpdateAvailable;
        private set => SetProperty(ref _isUpdateAvailable, value);
    }

    /// <summary>Texte affiché dans le bouton MAJ de la statusbar (ex : "↑ Update available: v1.2.0").</summary>
    public string UpdateAvailableText
    {
        get => _updateAvailableText;
        private set => SetProperty(ref _updateAvailableText, value);
    }

    /// <summary>URL de la page de release GitHub — ouverte au clic sur le bouton MAJ.</summary>
    public string? UpdateAvailableUrl { get; private set; }

    /// <summary>Ouvre la page de la release disponible dans le navigateur par défaut.</summary>
    public RelayCommand OpenUpdatePageCommand { get; private set; } = null!;

    // ── Propriétés de chargement lazy ─────────────────────────────────────

    /// <summary>Message de statut affiché dans l'overlay de chargement (ex : "Loading library…").</summary>
    public string LoadingStatusText
    {
        get => _loadingStatusText;
        private set => SetProperty(ref _loadingStatusText, value);
    }

    /// <summary>Progression du chargement (0–100) — bindée sur la ProgressBar de l'overlay.</summary>
    public double LoadingProgress
    {
        get => _loadingProgress;
        private set => SetProperty(ref _loadingProgress, value);
    }

    // ── Commandes ─────────────────────────────────────────────────────────

    /// <summary>Coche tous les systèmes et recalcule FilteredGames en un seul passage.</summary>
    public ICommand CheckAllSystemsCommand { get; private set; } = null!;

    /// <summary>Décoche tous les systèmes — FilteredGames devient vide.</summary>
    public ICommand UncheckAllSystemsCommand { get; private set; } = null!;

    /// <summary>Coche tous les jeux visibles dans FilteredGames.</summary>
    public ICommand SelectAllGamesCommand { get; private set; } = null!;

    /// <summary>Décoche tous les jeux visibles dans FilteredGames.</summary>
    public ICommand DeselectAllGamesCommand { get; private set; } = null!;

    /// <summary>Bascule l'affichage en mode mosaïque.</summary>
    public ICommand SwitchToMosaicCommand { get; private set; } = null!;

    /// <summary>Bascule l'affichage en mode liste.</summary>
    public ICommand SwitchToListCommand { get; private set; } = null!;

    /// <summary>Ouvre la fenêtre de Rebase avec les jeux sélectionnés.</summary>
    public ICommand RebaseCommand { get; private set; } = null!;

    /// <summary>Décoche tous les jeux de la bibliothèque, masqués par les filtres compris — bouton ✕ du badge de sélection.</summary>
    public ICommand ClearSelectionCommand { get; private set; } = null!;

    /// <summary>Ouvre un fichier de sélection (*.rsrgp) : coche ses jeux et reprend ses paramètres de rebase.</summary>
    public ICommand OpenPresetCommand { get; private set; } = null!;

    /// <summary>Enregistre la sélection dans son fichier, ou demande un nom s'il n'y en a pas encore.</summary>
    public ICommand SavePresetCommand { get; private set; } = null!;

    public ICommand SavePresetAsCommand { get; private set; } = null!;

    /// <summary>Détache la sélection de son fichier, sans décocher les jeux.</summary>
    public ICommand ClosePresetCommand { get; private set; } = null!;

    /// <summary>Resynchronise manuellement la base Derby depuis RomStation.</summary>
    public ICommand SyncDbCommand { get; private set; } = null!;

    /// <summary>Ouvre la fenêtre Paramètres en modal.</summary>
    public ICommand SettingsCommand { get; private set; } = null!;

    /// <summary>Commande d'ouverture de la fiche détail — paramètre attendu : GameId (int).</summary>
    public ICommand OpenGameDetailCommand { get; private set; } = null!;

    /// <summary>Scrolle vers le premier jeu dont le titre commence par la lettre passée en paramètre.</summary>
    public ICommand JumpToLetterCommand { get; private set; } = null!;

    // ── Constructeur ─────────────────────────────────────────────────────

    /// <summary>
    /// Constructeur principal — crée le VM vide.
    /// Le chargement réel des données se fait via LoadLibraryAsync()
    /// appelé depuis App.xaml.cs après l'affichage de MainWindow.
    /// </summary>
    public MainViewModel(UserPreferences preferences)
    {
        _preferences      = preferences;
        try { _metadata = _config.LoadAppMetadata(); } catch { /* métadonnées absentes : check MAJ désactivé */ }
        RefreshArchitectureWarning();
        // Initialisation du mode d'affichage depuis les préférences — défaut "Mosaic"
        _isMosaicView     = preferences.LastViewMode != "List";
        // Initialisation directe sur le champ pour éviter un SaveThumbnailSizePreference inutile au démarrage
        _thumbnailSize    = preferences.ThumbnailSize;
        // Initialisation directe sur le champ pour éviter un SaveSortCriteriaPreference inutile au démarrage
        _sortColumn       = preferences.LastSortCriteria;
        // Initialisation directe pour éviter les sauvegardes inutiles au démarrage
        _showIssuesOnly   = preferences.ShowIssuesOnly;
        _hideEmptySystems = preferences.HideEmptySystems;

        Preset = new RebasePresetSessionViewModel(
            () => Games.Where(g => g.IsSelected).ToList(),
            PresetSettingsFromPreferences)
        {
            LastDirectory = preferences.LastPresetDirectory,
        };
        Preset.PropertyChanged += (_, _) => RefreshPresetState();
        Preset.DirectoryUsed += dir =>
        {
            if (_preferences.LastPresetDirectory == dir) return;
            _preferences.LastPresetDirectory = dir;
            try { _config.SaveUserPreferences(_preferences); } catch { /* confort seulement */ }
        };
        Preset.SavedByUser += OfferPresetAssociation;

        // Initialisation de l'abécédaire — 26 lettres A-Z + # (tous désactivés par défaut)
        AlphabetItems = new ObservableCollection<AlphaItemViewModel>();
        for (char c = 'A'; c <= 'Z'; c++)
            AlphabetItems.Add(new AlphaItemViewModel(c.ToString()));
        AlphabetItems.Add(new AlphaItemViewModel("#"));

        InitCommands();
    }

    /// <summary>Initialise les commandes ICommand.</summary>
    private void InitCommands()
    {
        CheckAllSystemsCommand = new RelayCommand(() =>
        {
            foreach (var s in Systems) s.SetCheckedSilent(true);
            RefreshFilter();
        });
        UncheckAllSystemsCommand = new RelayCommand(() =>
        {
            foreach (var s in Systems) s.SetCheckedSilent(false);
            RefreshFilter();
        });

        SelectAllGamesCommand = new RelayCommand(() =>
            BulkSelect(() => { foreach (var g in FilteredGames) g.IsSelected = true; }));
        DeselectAllGamesCommand = new RelayCommand(() =>
            BulkSelect(() => { foreach (var g in FilteredGames) g.IsSelected = false; }));
        ClearSelectionCommand = new RelayCommand(() =>
            BulkSelect(() => { foreach (var g in Games) g.IsSelected = false; }),
            () => _selectedGameCount > 0);

        OpenPresetCommand   = new RelayCommand(OpenPreset, () => !_isLoading && Games.Count > 0);
        SavePresetCommand   = new RelayCommand(() => Preset.SaveInteractive(Application.Current.MainWindow, saveAs: false),
                                                  () => _selectedGameCount > 0);
        SavePresetAsCommand = new RelayCommand(() => Preset.SaveInteractive(Application.Current.MainWindow, saveAs: true),
                                                  () => _selectedGameCount > 0);
        ClosePresetCommand  = new RelayCommand(ClosePreset, () => Preset.HasFile);

        SwitchToMosaicCommand = new RelayCommand(() => IsMosaicView = true);
        SwitchToListCommand   = new RelayCommand(() => IsMosaicView = false);

        RebaseCommand = new RelayCommand(
            execute: () =>
            {
                var selected = Games.Where(g => g.IsSelected).ToList();
                // ViewModel passé au constructeur pour que l'injection soit immédiate
                // Toute la bibliothèque est transmise pour départager les homonymes de façon stable
                var vm  = new RebaseViewModel(selected, Games.ToList(), _romStationPath, _dbCopyPath, _preferences,
                                              Systems.Select(s => s.Name).ToList(), Preset, SystemIconsByName());
                var win = new RebaseWindow(vm)
                {
                    Owner = Application.Current.MainWindow,
                };
                win.ShowDialog();
                RefreshArchitectureWarning(); // l'éditeur d'architectures est accessible depuis cette fenêtre
            },
            canExecute: () => Games.Any(g => g.IsSelected));

        SyncDbCommand = new RelayCommand(
            execute: async () => await SyncDbAsync(),
            canExecute: () => !_isLoading);

        SettingsCommand = new RelayCommand(OpenSettings);

        OpenGameDetailCommand = new RelayCommand(param => { if (param is int id) OpenGameDetail(id); });

        OpenUpdatePageCommand = new RelayCommand(OpenUpdatePage);

        // Délégation du scroll vers la View via le callback injecté (ScrollToLetter).
        // CanExecute bloqué si le tri n'est pas alphabétique par titre — l'abécédaire est inopérant dans ce cas.
        JumpToLetterCommand = new RelayCommand<string>(
            execute: letter =>
            {
                if (string.IsNullOrEmpty(letter)) return;
                ScrollToLetter?.Invoke(letter);
            },
            canExecute: _ => _sortColumn == "Title");
    }

    /// <summary>
    /// Bascule ou change la colonne de tri, puis retrie FilteredGames.
    /// Appelé depuis MainWindow.xaml.cs sur clic d'en-tête de colonne.
    /// </summary>
    public void SetSort(string column)
    {
        if (SortColumn == column)
            SortAscending = !SortAscending;
        else
        {
            SortColumn    = column;
            SortAscending = true;
        }
        ApplySort();
    }

    /// <summary>
    /// Retrie FilteredGames en place sans refiltrer depuis la liste complète Games.
    /// Plus léger que RefreshFilter() — utilisé uniquement quand seul le tri change.
    /// </summary>
    public void ApplySort()
    {
        var sorted = (_sortAscending
            ? FilteredGames.OrderBy(GetSortKey(_sortColumn))
            : FilteredGames.OrderByDescending(GetSortKey(_sortColumn)))
            .ToList();
        FilteredGames = new ObservableCollection<GameItemViewModel>(sorted);
    }

    /// <summary>Renvoie le sélecteur de clé de tri correspondant à la colonne demandée.</summary>
    private static Func<GameItemViewModel, string> GetSortKey(string column) => column switch
    {
        "System" => g => g.SystemName,
        "Files"  => g => g.FileCount.ToString("D5"),
        _        => g => g.Title,
    };

    // ── Chargement lazy ───────────────────────────────────────────────────

    /// <summary>
    /// Charge les systèmes et les jeux depuis la copie Derby et les injecte progressivement dans les collections.
    /// Appelé depuis App.xaml.cs après l'affichage de MainWindow.
    /// Met à jour LoadingProgress par batch de 50 jeux pour animer la ProgressBar dans la vue.
    /// </summary>
    /// <param name="dbCopyPath">Chemin de la copie locale de la base Derby.</param>
    /// <param name="romStationPath">Chemin de l'installation RomStation (pour les images).</param>
    /// <param name="systemsToCheck">systemsToCheck null → tous cochés (démarrage applicatif).
    /// HashSet fourni → coché uniquement si le nom y figure (restauration après Sync).</param>
    public async Task LoadLibraryAsync(string dbCopyPath, string romStationPath,
        HashSet<string>? systemsToCheck = null)
    {
        _romStationPath   = romStationPath;
        _dbCopyPath       = dbCopyPath;
        IsLoading         = true;
        LoadingStatusText = Strings.Splash_LoadingLibrary;
        LoadingProgress   = 0;

        // Étape 1 — Charger les données et animer la barre en parallèle.
        // SimulateProgressAsync démarre immédiatement (sans connaître le nombre réel de jeux)
        // et dure au minimum 500ms ; les tâches Derby tournent en même temps.
        var gamesTask    = Task.Run(() => _derby.GetGames(dbCopyPath, romStationPath));
        var systemsTask  = Task.Run(() => _derby.GetSystems(dbCopyPath, romStationPath));
        var progressTask = SimulateProgressAsync(0);
        await Task.WhenAll(gamesTask, systemsTask, progressTask);

        var games   = gamesTask.Result;
        var systems = systemsTask.Result;

        // Calcule le nombre de jeux par système pour les badges de la sidebar
        var countBySystem = games
            .GroupBy(g => g.SystemName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        // Étape 3a — Insérer tous les jeux sur le thread UI et afficher la barre à 100%.
        // Un seul bloc measure/arrange WPF au lieu de N passages pour N batches.
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            foreach (var sys in systems)
            {
                int count = countBySystem.TryGetValue(sys.Name, out int c) ? c : 0;
                bool isChecked = systemsToCheck == null || systemsToCheck.Contains(sys.Name);
                Systems.Add(new SystemItemViewModel(sys.Name, sys.ImagePath, count, RefreshFilter, isChecked));
            }

            var gameVms = games.Select(g => new GameItemViewModel(
                g.Id, g.Rid, g.Title, g.SystemName, g.SystemImagePath,
                g.CoverPath, g.CoverExists, g.FileExists,
                g.FileCount, g.GameDirectory, false, false, RefreshSelectedCount)).ToList();

            foreach (var vm in gameVms) Games.Add(vm);
            FilteredGames = new ObservableCollection<GameItemViewModel>(gameVms);

            _lastSyncDate = DateTime.Now;
            OnPropertyChanged(nameof(LastSyncText));
            OnPropertyChanged(nameof(FilteredGameCount));

            // Mise à jour du badge d'issues et auto-reset du filtre si aucun problème détecté
            OnPropertyChanged(nameof(HasAnyIssues));
            if (!HasAnyIssues && _showIssuesOnly)
            {
                _showIssuesOnly = false;
                OnPropertyChanged(nameof(ShowIssuesOnly));
            }
            RefreshVisibleSystems();

        }, System.Windows.Threading.DispatcherPriority.Normal);

        // Étape 3b — Passer à 100% sur le thread UI au niveau Render.
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            LoadingProgress = 100;
        }, System.Windows.Threading.DispatcherPriority.Render);

        // Étape 3c — Forcer WPF à rendre le frame avec la barre à 100%.
        await Application.Current.Dispatcher.InvokeAsync(
            () => { },
            System.Windows.Threading.DispatcherPriority.Render);

        // Étape 3d — Laisser voir le 100% pendant 350ms.
        await Task.Delay(350);

        // Étape 3e — Cacher l'overlay et appliquer les filtres éventuellement actifs.
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            IsLoading = false;
            RefreshFilter();
        }, System.Windows.Threading.DispatcherPriority.Normal);

        // Check de MAJ en arrière-plan : fire-and-forget intentionnel.
        _ = CheckForUpdateInBackgroundAsync();
    }

    /// <summary>
    /// Anime la barre de progression de 0 à 95% en 50 étapes régulières.
    /// Durée estimée selon le nombre de jeux (~500ms pour 900 jeux).
    /// Purement cosmétique — découplée du chargement Derby réel.
    /// </summary>
    private async Task SimulateProgressAsync(int gameCount)
    {
        int estimatedMs = Math.Max(800, gameCount / 2);
        int steps       = 60;
        int stepMs      = estimatedMs / steps;

        for (int i = 1; i <= steps; i++)
        {
            await Task.Delay(stepMs);
            double progress = (double)i / steps * 95;

            // LoadingProgress est une propriété WPF bindée — doit être mise à jour sur le thread UI.
            // DispatcherPriority.Render garantit le traitement avant le frame de rendu suivant.
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                LoadingProgress = progress;
            }, System.Windows.Threading.DispatcherPriority.Render);
        }
    }

    // ── Synchronisation manuelle ──────────────────────────────────────────

    /// <summary>
    /// Resynchronise la base Derby depuis RomStation sans quitter l'application.
    /// Affiche une popup de confirmation, affiche l'overlay de chargement, recopie la base,
    /// puis recharge systèmes et jeux via LoadLibraryAsync en préservant le filtre système.
    /// </summary>
    public async Task SyncDbAsync()
    {
        if (_isLoading) return; // garde-fou si déjà en cours

        if (string.IsNullOrWhiteSpace(_romStationPath) || string.IsNullOrWhiteSpace(_dbCopyPath))
            return;

        // Confirmation avant Sync — prévient l'utilisateur que sa sélection sera perdue.
        // Filtres systèmes préservés, sélection perdue (inévitable).
        string libelle = SelectedGameCount == 1
            ? Strings.Sync_Confirm_Singular
            : Strings.Sync_Confirm_Plural;
        string message = SelectedGameCount > 0
            ? string.Format(Strings.Sync_Confirm_MessageWithSelection, SelectedGameCount, libelle)
            : Strings.Sync_Confirm_Message;

        var confirm = new ConfirmDialog(
            Strings.Sync_Confirm_Title,
            message,
            Strings.Sync_Confirm_Proceed,
            Strings.Sync_Confirm_Cancel)
            { Owner = Application.Current.MainWindow };
        confirm.ShowDialog();
        if (!confirm.Result) return;

        IsLoading         = true;
        LoadingStatusText = Strings.Sync_InProgress;
        LoadingProgress   = 0;

        // Callback de progression : met à jour le texte de l'overlay pendant la copie
        var progress = new Progress<string>(msg => LoadingStatusText = msg);

        // Shutdown de la base Derby locale avant la copie.
        // Sans ça, db.lck reste verrouillé par la JVM et la suppression du dossier échoue.
        await Task.Run(() => _derby.ShutdownDatabase(_dbCopyPath));

        bool copyOk;
        try
        {
            copyOk = await _rs.CopyDatabaseAsync(_romStationPath, _dbCopyPath, progress);
        }
        catch (Exception)
        {
            copyOk = false;
        }

        if (!copyOk)
        {
            IsLoading = false;
            var dialog = new ConfirmDialog(
                Strings.Sync_ErrorTitle,
                ErrorCodes.Tag(Strings.Splash_DBLocked, ErrorCodes.SyncFailed),
                "OK",
                null) { Owner = Application.Current.MainWindow };
            dialog.ShowDialog();
            return;
        }

        // Capture des systèmes cochés avant Clear — préservés par nom après rechargement
        var systemesPreserves = Systems
            .Where(s => s.IsChecked)
            .Select(s => s.Name)
            .ToHashSet();

        // Vider les collections existantes avant le rechargement
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            Systems.Clear();
            Games.Clear();
            FilteredGames    = new ObservableCollection<GameItemViewModel>();
            VisibleSystems   = new ObservableCollection<SystemItemViewModel>();
        });

        // Rechargement complet avec état des filtres pré-configuré — IsLoading sera remis à
        // false en fin de LoadLibraryAsync, LastSyncText sera automatiquement mis à jour.
        await LoadLibraryAsync(_dbCopyPath, _romStationPath, systemesPreserves);
    }

    // ── Filtrage ──────────────────────────────────────────────────────────

    /// <summary>
    /// Recalcule FilteredGames selon les systèmes cochés, la recherche textuelle et le filtre "Problèmes".
    /// Dispatcher.Invoke garantit que les modifications de collection se font sur le thread UI,
    /// même si RefreshFilter est appelé depuis un thread de fond.
    /// </summary>
    private void RefreshFilter()
    {
        // Ignore les appels pendant le chargement — les cases système ne doivent pas
        // déclencher un recalcul massif pendant que les jeux arrivent par batch.
        if (_isLoading) return;

        Application.Current.Dispatcher.Invoke(() =>
        {
            var checkedSystems = Systems
                .Where(s => s.IsChecked)
                .Select(s => s.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Le filtre ne touche pas à la sélection : masquer un système ne décoche pas ses jeux.
            // La sélection reste visible dans le badge du header et les badges de la sidebar.

            // Remplacement atomique — un seul CollectionChanged au lieu de N Clear/Add
            if (checkedSystems.Count == 0)
            {
                FilteredGames = new ObservableCollection<GameItemViewModel>();
            }
            else
            {
                string search = _searchText.Trim();
                var filtered = Games.Where(g =>
                    checkedSystems.Contains(g.SystemName) &&
                    (search.Length == 0 || g.Title.Contains(search, StringComparison.OrdinalIgnoreCase)) &&
                    (!_showIssuesOnly || g.HasIssues));

                IEnumerable<GameItemViewModel> sorted = _sortColumn switch
                {
                    "System" => _sortAscending
                        ? filtered.OrderBy(g => g.SystemName).ThenBy(g => g.Title)
                        : filtered.OrderByDescending(g => g.SystemName).ThenBy(g => g.Title),
                    "Files"  => _sortAscending
                        ? filtered.OrderBy(g => g.FileCount).ThenBy(g => g.Title)
                        : filtered.OrderByDescending(g => g.FileCount).ThenBy(g => g.Title),
                    _        => _sortAscending
                        ? filtered.OrderBy(g => g.Title)
                        : filtered.OrderByDescending(g => g.Title),
                };
                FilteredGames = new ObservableCollection<GameItemViewModel>(sorted);
            }

            RefreshSelectedCount();
            UpdateAlphabetStates();
        });
    }

    /// <summary>
    /// Recalcule le nombre de jeux sélectionnés à partir de la liste complète, et le détail par système
    /// pour les badges de la sidebar — la sélection est globale, indépendante des filtres.
    /// </summary>
    private void RefreshSelectedCount()
    {
        if (_bulkSelection) return; // un seul recalcul à la fin d'une opération groupée

        SelectedGameCount = Games.Count(g => g.IsSelected);
        Preset.RefreshDirty();
        RefreshPresetState();

        var bySystem = Games.Where(g => g.IsSelected)
            .GroupBy(g => g.SystemName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(grp => grp.Key, grp => grp.Count(), StringComparer.OrdinalIgnoreCase);
        foreach (var sys in Systems)
            sys.SelectedCount = bySystem.GetValueOrDefault(sys.Name);
    }

    // ── Fichiers de sélection ─────────────────────────────────────────────

    private bool _bulkSelection;

    /// <summary>Sélection de travail : fichier courant, paramètres du rebase et règles par jeu, partagés avec la fenêtre de rebase.</summary>
    public RebasePresetSessionViewModel Preset { get; }

    /// <summary>Coche ou décoche en lot : chaque jeu notifierait sinon un recalcul complet des compteurs et de l'état « modifié ».</summary>
    private void BulkSelect(Action change)
    {
        _bulkSelection = true;
        try { change(); }
        finally { _bulkSelection = false; }
        RefreshSelectedCount();

        // WPF ne réévalue l'état des boutons qu'à la prochaine action de l'utilisateur : une présélection ouverte
        // par double-clic laisserait « Rebase vers… » grisé jusqu'au premier mouvement de souris
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>Paramètres enregistrés tant que la fenêtre de rebase n'a pas été ouverte : les derniers utilisés.</summary>
    private RebasePresetSettings PresetSettingsFromPreferences()
    {
        string label = string.Empty;
        try
        {
            label = new ArchitectureService().LoadArchitectures()
                .FirstOrDefault(a => a.Id == _preferences.LastRebaseArchitectureId)?.Label ?? string.Empty;
        }
        catch { /* le libellé ne sert qu'aux messages */ }

        return new RebasePresetSettings
        {
            TargetPath        = _preferences.LastRebaseTargetPath,
            ArchitectureId    = _preferences.LastRebaseArchitectureId,
            ArchitectureLabel = label,
            ArchiveMode       = _preferences.LastRebaseArchiveMode switch { "Copy" => "Copy", "ExtractAll" => "ExtractAll", _ => "ExtractRequired" },
            ExtractLayout     = _preferences.LastRebaseExtractLayout == "Subfolder" ? "Subfolder" : "Auto",
            CopyCovers        = _preferences.LastRebaseCopyCovers,
            GenerateGamelist  = _preferences.LastRebaseGenerateGamelist,
            BackupGamelist    = _preferences.LastRebaseBackupGamelist,
            Convert           = _preferences.LastRebaseConvert,
            MetadataLanguage  = _preferences.LastRebaseMetadataLanguage is "fr" or "en" ? _preferences.LastRebaseMetadataLanguage : "auto",
            DuplicatePolicy   = _preferences.DuplicatePolicy == "Overwrite" ? "Overwrite" : "Ignore",
            MaxParallelCopies = _preferences.MaxParallelCopies,
            RetryCount        = _preferences.RetryCount,
            RetryDelaySeconds = _preferences.RetryDelaySeconds,
        };
    }

    /// <summary>
    /// Avant d'abandonner la sélection courante (autre fichier, fermeture du fichier, sortie de l'application) :
    /// propose d'enregistrer ses modifications. False si l'utilisateur renonce à l'action.
    /// </summary>
    public bool ConfirmDiscardPresetChanges()
    {
        if (!Preset.HasFile || !Preset.IsDirty) return true;

        // Plus aucun jeu coché (après une synchronisation, par exemple) : rien qui vaille d'être enregistré, et surtout pas un fichier vidé
        if (_selectedGameCount == 0) return true;

        var dialog = new ConfirmDialog(
            Strings.Preset_Unsaved_Title,
            string.Format(Strings.Preset_Unsaved_Message, Preset.FileName),
            Strings.Preset_Unsaved_Save,
            Strings.Preset_Unsaved_Discard,
            Strings.Common_Cancel) { Owner = Application.Current.MainWindow };
        dialog.ShowDialog();

        return dialog.Choice switch
        {
            ConfirmChoice.Primary   => Preset.SaveInteractive(Application.Current.MainWindow, saveAs: false, announce: false),
            ConfirmChoice.Secondary => true,
            _                       => false,
        };
    }

    /// <summary>
    /// Après le premier enregistrement d'une présélection, propose une fois d'associer les .rsrgp à cette copie de RSR.
    /// L'écriture se fait sous HKCU, sans droits administrateur. La réponse, quelle qu'elle soit, clôt la question :
    /// le bouton des Paramètres reste le moyen d'y revenir.
    /// </summary>
    private void OfferPresetAssociation(Window? owner)
    {
        if (Environment.ProcessPath is not { } exe) return;

        PresetAssociationState state;
        try   { state = FileAssociationService.GetState(exe); }
        catch { return; }
        if (!FileAssociationService.ShouldOffer(_preferences.PresetAssociationOffered, state)) return;

        _preferences.PresetAssociationOffered = true;
        try { _config.MarkPresetAssociationOffered(); } catch { /* la question sera reposée, sans gravité */ }

        var offer = new ConfirmDialog(
            Strings.Preset_AssociateOffer_Title,
            Strings.Preset_AssociateOffer_Message,
            Strings.Preset_AssociateOffer_Accept,
            Strings.Preset_AssociateOffer_Decline) { Owner = owner ?? Application.Current.MainWindow };
        offer.ShowDialog();
        if (offer.Choice != ConfirmChoice.Primary) return;

        try
        {
            FileAssociationService.Register(exe, Strings.Preset_FileFilter);
        }
        catch (Exception ex)
        {
            new ConfirmDialog(
                Strings.Preset_AssociateError_Title,
                ErrorCodes.Tag(string.Format(Strings.Settings_Presets_Error, ex.Message), ErrorCodes.PresetAssociationFailed),
                "OK") { Owner = owner ?? Application.Current.MainWindow }.ShowDialog();
        }
    }

    private void OpenPreset()
    {
        var owner = Application.Current.MainWindow;

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title  = Strings.Preset_Open_DialogTitle,
            Filter = RebasePresetSessionViewModel.FileDialogFilter,
        };
        if (!string.IsNullOrEmpty(Preset.LastDirectory) && System.IO.Directory.Exists(Preset.LastDirectory))
            dialog.InitialDirectory = Preset.LastDirectory;
        if (dialog.ShowDialog(owner) != true) return;

        OpenPresetFile(dialog.FileName);
    }

    /// <summary>Ouvre le fichier donné. Public pour un futur glisser-déposer ou une ouverture en ligne de commande.</summary>
    public void OpenPresetFile(string path)
    {
        var owner = Application.Current.MainWindow;

        RebasePreset file;
        try
        {
            file = RebasePresetService.Load(path);
        }
        catch (RebasePresetException ex)
        {
            string reason = ex.Error switch
            {
                RebasePresetError.NotASelection => ErrorCodes.Tag(Strings.Preset_OpenError_NotASelection, ErrorCodes.PresetNotAPreset),
                RebasePresetError.InvalidJson   => ErrorCodes.Tag(string.Format(Strings.Preset_OpenError_InvalidJson, ex.Message), ErrorCodes.PresetInvalidJson),
                _                                => ErrorCodes.Tag(string.Format(Strings.Preset_OpenError_Unreadable, ex.Message), ErrorCodes.PresetUnreadable),
            };
            new ConfirmDialog(Strings.Preset_OpenError_Title,
                $"{System.IO.Path.GetFileName(path)}\n\n{reason}", "OK") { Owner = owner }.ShowDialog();
            return;
        }

        var library = Games.Select(g => new LibraryGameRef(g.Id, g.Rid, g.Title, g.SystemName)).ToList();
        var match   = RebasePresetService.Match(file, library);

        if (match.Matched.Count == 0)
        {
            new ConfirmDialog(Strings.Preset_OpenError_Title,
                ErrorCodes.Tag(string.Format(Strings.Preset_OpenError_NoGame, System.IO.Path.GetFileName(path), file.Games.Count), ErrorCodes.PresetNoGameFound),
                "OK") { Owner = owner }.ShowDialog();
            return;
        }

        // Une autre présélection est chargée et modifiée : proposer de l'enregistrer avant de la quitter.
        // Placé ici et non avant la boîte Ouvrir, pour valoir aussi pour un double-clic dans l'Explorateur.
        if (!ConfirmDiscardPresetChanges()) return;

        // Des jeux cochés à la main, hors de toute présélection, seraient perdus sans prévenir
        if (!Preset.HasFile && _selectedGameCount > 0)
        {
            var replace = new ConfirmDialog(
                Strings.Preset_Replace_Title,
                string.Format(Strings.Preset_Replace_Message, _selectedGameCount, match.Matched.Count),
                Strings.Preset_Replace_Proceed,
                Strings.Common_Cancel) { Owner = owner };
            replace.ShowDialog();
            if (!replace.Result) return;
        }

        // Les écarts s'annoncent avant de rien toucher : l'utilisateur peut encore renoncer
        var discrepancies = PresetDiscrepancies(file, match);
        if (discrepancies.Count > 0)
        {
            string summary = string.Format(Strings.Preset_Opened_Summary, match.Matched.Count, file.Games.Count);
            var notice = new ConfirmDialog(Strings.Preset_Opened_Title,
                summary + "\n\n" + string.Join("\n\n", discrepancies),
                Strings.Preset_Discrepancy_Continue,
                Strings.Preset_Discrepancy_Cancel) { Owner = owner };
            notice.ShowDialog();
            if (!notice.Result) return;
        }

        BulkSelect(() =>
        {
            foreach (var g in Games)
                g.IsSelected = match.Matched.ContainsKey(g.Id);
        });

        var overrides = new Dictionary<int, GameRuleOverrides>();
        foreach (var g in Games.Where(g => match.Matched.ContainsKey(g.Id)))
        {
            var saved = match.Matched[g.Id];
            if (saved.HasOverride)
                overrides[g.Rid] = new GameRuleOverrides(saved.KeepFileName, saved.M3U, saved.Extract, saved.Transform);
        }

        Preset.Adopt(path, file.Settings, overrides);
        Preset.NotifyDirectoryUsed(path);
        RefreshPresetState();

        // La fenêtre de rebase ne s'ouvre pas d'elle-même (essayé, écarté par le dev) : ouvrir une présélection coche
        // ses jeux et retient ses paramètres, l'utilisateur voit le résultat dans la bibliothèque et décide de la suite.
    }

    /// <summary>Referme la présélection : ses jeux sont décochés et la fenêtre de rebase reviendra aux derniers paramètres utilisés sans présélection.</summary>
    private void ClosePreset()
    {
        if (!ConfirmDiscardPresetChanges()) return;
        Preset.Close();
        BulkSelect(() => { foreach (var g in Games) g.IsSelected = false; });
        RefreshPresetState();
    }

    /// <summary>Le bouton Enregistrer n'a de raison d'être qu'avec des jeux cochés ou une présélection chargée : sinon il est masqué, pas grisé.</summary>
    public bool ShowSavePreset => _selectedGameCount > 0 || Preset.HasFile;

    /// <summary>Démarrage à froid : rien de coché, aucune présélection. L'en-tête de la bibliothèque propose alors d'en ouvrir une.</summary>
    public bool ShowOpenPresetHint => _selectedGameCount == 0 && !Preset.HasFile && !_isLoading;

    /// <summary>Suffixe de la barre de titre : « — tests.rsrgp • ». Vide sans présélection.</summary>
    public string PresetTitleSuffix => Preset.HasFile ? "  —  " + Preset.DisplayText : string.Empty;

    /// <summary>Texte de la zone Présélection de l'en-tête : le nom du fichier chargé (et son « • »), ou « Aucune chargée ».</summary>
    public string PresetZoneText => Preset.HasFile ? Preset.DisplayText : Strings.Preset_Zone_None;

    /// <summary>Infobulle de la zone : le chemin complet du fichier, ou ce qu'est une présélection quand aucune n'est chargée.</summary>
    public string PresetZoneTooltip => Preset.HasFile
        ? Preset.FilePath + "\n" + Strings.Preset_File_Tooltip
        : Strings.Preset_Zone_None_Tooltip;

    private void RefreshPresetState()
    {
        OnPropertyChanged(nameof(PresetZoneText));
        OnPropertyChanged(nameof(PresetZoneTooltip));
        OnPropertyChanged(nameof(ShowSavePreset));
        OnPropertyChanged(nameof(ShowOpenPresetHint));
        OnPropertyChanged(nameof(PresetTitleSuffix));
    }

    /// <summary>Tout ce que l'utilisateur doit savoir sur l'écart entre le fichier et l'état présent de RSR et de la bibliothèque.</summary>
    private List<string> PresetDiscrepancies(RebasePreset file, RebasePresetMatch match)
    {
        const int MaxListed = 8;
        var warnings = new List<string>();

        if (file.Version > RebasePreset.CurrentVersion)
            warnings.Add(Strings.Preset_Warn_NewerVersion);

        if (match.Missing.Count > 0)
        {
            var lines = match.Missing.Take(MaxListed).Select(g => $"  •  {g.Title} ({g.System})").ToList();
            if (match.Missing.Count > MaxListed)
                lines.Add("  " + string.Format(Strings.Preset_Warn_AndMore, match.Missing.Count - MaxListed));
            warnings.Add(string.Format(Strings.Preset_Warn_MissingGames, match.Missing.Count) + "\n" + string.Join("\n", lines));
        }

        if (match.MatchedByTitle.Count > 0)
            warnings.Add(string.Format(Strings.Preset_Warn_MatchedByTitle, match.MatchedByTitle.Count));

        var settings = file.Settings;
        if (!string.IsNullOrWhiteSpace(settings.ArchitectureId))
        {
            bool known = false;
            try { known = new ArchitectureService().LoadArchitectures().Any(a => a.Id == settings.ArchitectureId); }
            catch { /* traité comme une architecture absente */ }
            if (!known)
                warnings.Add(string.Format(Strings.Preset_Warn_MissingArchitecture,
                    string.IsNullOrWhiteSpace(settings.ArchitectureLabel) ? settings.ArchitectureId : settings.ArchitectureLabel));
        }

        if (!string.IsNullOrWhiteSpace(settings.TargetPath) && !System.IO.Directory.Exists(settings.TargetPath))
            warnings.Add(string.Format(Strings.Preset_Warn_MissingTarget, settings.TargetPath));


        return warnings;
    }

    /// <summary>
    /// Met à jour l'état IsEnabled de chaque item de l'abécédaire selon le contenu courant de FilteredGames.
    /// Désactive toutes les lettres si le tri actif n'est pas alphabétique par titre.
    /// </summary>
    private void UpdateAlphabetStates()
    {
        bool triAlphabetique = _sortColumn == "Title";

        if (!triAlphabetique)
        {
            // Tri non alphabétique : abécédaire entièrement désactivé
            foreach (var item in AlphabetItems)
                item.IsEnabled = false;
            return;
        }

        // Construction du set des lettres représentées dans FilteredGames
        var lettresPresentes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var game in FilteredGames)
        {
            if (string.IsNullOrEmpty(game.Title)) continue;

            char premiere = game.Title[0];
            string bucket = char.IsLetter(premiere) && char.ToUpper(premiere) is >= 'A' and <= 'Z'
                ? char.ToUpper(premiere).ToString()
                : "#";

            lettresPresentes.Add(bucket);
        }

        foreach (var item in AlphabetItems)
            item.IsEnabled = lettresPresentes.Contains(item.Letter);
    }

    /// <summary>Ouvre la fiche détail du jeu dont l'identifiant Derby est passé en paramètre.</summary>
    private void OpenGameDetail(int gameId)
    {
        var detail = _derby.GetGameDetail(_dbCopyPath, _romStationPath, gameId, ResolveLocaleTag());
        if (detail is null) return;

        var vm  = new GameDetailViewModel(detail, _romStationPath);
        var win = new Views.Dialogs.GameDetailWindow(vm)
        {
            Owner = Application.Current.MainWindow,
        };
        win.ShowDialog();
    }

    /// <summary>Retourne le tag de locale Derby ("fr" ou "en") selon la culture courante du thread UI.</summary>
    private static string ResolveLocaleTag()
        => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr" ? "fr" : "en";

    /// <summary>Ouvre la page de la release disponible dans le navigateur par défaut.</summary>
    private void OpenUpdatePage()
    {
        if (string.IsNullOrWhiteSpace(UpdateAvailableUrl)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName        = UpdateAvailableUrl,
                UseShellExecute = true,
            });
        }
        catch
        {
            // Silencieux si le navigateur ne peut pas être lancé.
        }
    }

    /// <summary>
    /// Lance un check de MAJ en arrière-plan avec throttle 24h. Met à jour IsUpdateAvailable et
    /// UpdateAvailableText sur le thread UI si une MAJ est détectée. Silencieux dans tous les autres cas.
    /// </summary>
    private async Task CheckForUpdateInBackgroundAsync()
    {
        try
        {
            var state = _config.LoadAppState();

            // Throttle 24h : réutilise le résultat persisté si le check est récent.
            if (state.LastUpdateCheckUtc.HasValue &&
                DateTime.UtcNow - state.LastUpdateCheckUtc.Value < TimeSpan.FromHours(24))
            {
                Application.Current?.Dispatcher.Invoke(() => ApplyPersistedUpdateState(state));
                return;
            }

            // Date sauvegardée AVANT l'appel HTTP : évite la boucle de retry si l'app redémarre pendant le check.
            state.LastUpdateCheckUtc = DateTime.UtcNow;
            _config.SaveAppState(state);

            var service = new UpdateCheckService(_metadata);
            var result  = await service.CheckAsync().ConfigureAwait(false);

            if (result.Outcome == UpdateCheckOutcome.UpdateAvailable)
            {
                state.LastAvailableVersion = result.AvailableVersion;
                state.LastUpdateUrl        = result.ReleaseUrl;
            }
            else if (result.Outcome == UpdateCheckOutcome.UpToDate)
            {
                state.LastAvailableVersion = null;
                state.LastUpdateUrl        = null;
            }
            // Sur erreur, on préserve l'ancien état persisté.

            _config.SaveAppState(state);
            Application.Current?.Dispatcher.Invoke(() => ApplyPersistedUpdateState(state));
        }
        catch
        {
            // Silencieux total — check de fond non critique.
        }
    }

    private void ApplyPersistedUpdateState(AppState state)
    {
        if (UpdateCheckService.IsRemoteVersionNewer(state.LastAvailableVersion))
        {
            UpdateAvailableUrl  = state.LastUpdateUrl;
            UpdateAvailableText = string.Format(Strings.StatusBar_UpdateAvailable, state.LastAvailableVersion);
            IsUpdateAvailable   = true;
        }
        else
        {
            // Nettoyage : si une version était persistée mais n'est plus valide (périmée ou désormais
            // inférieure à la version courante), on la retire du fichier d'état pour éviter la régression
            // au prochain démarrage.
            if (!string.IsNullOrWhiteSpace(state.LastAvailableVersion))
            {
                state.LastAvailableVersion = null;
                state.LastUpdateUrl        = null;
                try { _config.SaveAppState(state); } catch { }
            }
            IsUpdateAvailable   = false;
            UpdateAvailableUrl  = null;
            UpdateAvailableText = string.Empty;
        }
    }

    /// <summary>Icône de chaque console par nom de système, pour la table des systèmes de l'éditeur d'architectures.</summary>
    private Dictionary<string, string> SystemIconsByName()
    {
        var icons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var system in Systems)
            if (!string.IsNullOrWhiteSpace(system.ImagePath))
                icons[system.Name] = system.ImagePath;
        return icons;
    }

    /// <summary>Ouvre le panneau de paramètres en modal.</summary>
    private void OpenSettings()
    {
        var vm  = new SettingsViewModel(_preferences, Systems.Select(s => s.Name).ToList(), SystemIconsByName());
        var win = new Views.Dialogs.SettingsWindow(vm)
        {
            Owner = Application.Current.MainWindow,
        };
        win.ShowDialog();
        RefreshArchitectureWarning(); // l'éditeur d'architectures est accessible depuis les Paramètres
    }

    /// <summary>Sauvegarde le mode d'affichage courant dans UserPreferences. Silencieux en cas d'erreur.</summary>
    private void SaveViewModePreference()
    {
        try
        {
            _preferences.LastViewMode = _isMosaicView ? "Mosaic" : "List";
            _config.SaveUserPreferences(_preferences);
        }
        catch
        {
            // Ne pas bloquer l'UI si l'écriture disque échoue
        }
    }

    /// <summary>Sauvegarde la taille des vignettes dans UserPreferences. Silencieux en cas d'erreur.</summary>
    private void SaveThumbnailSizePreference()
    {
        try
        {
            _preferences.ThumbnailSize = _thumbnailSize;
            _config.SaveUserPreferences(_preferences);
        }
        catch
        {
            // Ne pas bloquer l'UI si l'écriture disque échoue
        }
    }

    /// <summary>Reconstruit VisibleSystems depuis Systems selon le filtre HideEmptySystems.</summary>
    private void RefreshVisibleSystems()
    {
        IEnumerable<SystemItemViewModel> source = _hideEmptySystems
            ? Systems.Where(s => s.GameCount > 0)
            : Systems;
        VisibleSystems = new ObservableCollection<SystemItemViewModel>(source);
    }

    // Utilisé UNIQUEMENT par la ComboBox "Trier par" dans la toolbar.
    // Distinct de SetSort qui gère le toggle asc/desc des en-têtes de
    // colonnes cliquables. Force toujours le sens ascendant.
    private void SetSortCriteria(string column)
    {
        SortColumn    = column;   // INPC + arrows + SortCriteria + UpdateAlphabetStates()
        SortAscending = true;     // force ascendant
        ApplySort();
        SaveSortCriteriaPreference();
    }

    /// <summary>Sauvegarde le critère de tri dans UserPreferences. Silencieux en cas d'erreur.</summary>
    private void SaveSortCriteriaPreference()
    {
        try
        {
            _preferences.LastSortCriteria = _sortColumn;
            _config.SaveUserPreferences(_preferences);
        }
        catch
        {
            // Ne pas bloquer l'UI si l'écriture disque échoue
        }
    }

    /// <summary>Sauvegarde HideEmptySystems dans UserPreferences. Silencieux en cas d'erreur.</summary>
    private void SaveHideEmptySystemsPreference()
    {
        try
        {
            _preferences.HideEmptySystems = _hideEmptySystems;
            _config.SaveUserPreferences(_preferences);
        }
        catch
        {
            // Ne pas bloquer l'UI si l'écriture disque échoue
        }
    }

    /// <summary>Sauvegarde ShowIssuesOnly dans UserPreferences. Silencieux en cas d'erreur.</summary>
    private void SaveShowIssuesOnlyPreference()
    {
        try
        {
            _preferences.ShowIssuesOnly = _showIssuesOnly;
            _config.SaveUserPreferences(_preferences);
        }
        catch
        {
            // Ne pas bloquer l'UI si l'écriture disque échoue
        }
    }
}
