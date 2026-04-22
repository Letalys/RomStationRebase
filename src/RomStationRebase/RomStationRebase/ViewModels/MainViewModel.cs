using System.Collections.ObjectModel;
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

    private string    _romStationPath    = string.Empty;
    private string    _dbCopyPath        = string.Empty;
    private string    _searchText        = string.Empty;
    private bool      _isMosaicView      = true;
    private bool      _showIssuesOnly    = false;
    private string    _thumbnailSize     = "Normal";
    private bool      _isLoading         = true;   // true par défaut — overlay visible jusqu'à la fin de LoadLibraryAsync
    private int       _selectedGameCount;
    private DateTime? _lastSyncDate;
    private string    _loadingStatusText = string.Empty;
    private double    _loadingProgress;
    private string    _sortColumn        = "Title";
    private bool      _sortAscending     = true;

    // ── Collections ───────────────────────────────────────────────────────

    /// <summary>Liste des systèmes affichés dans la sidebar.</summary>
    public ObservableCollection<SystemItemViewModel> Systems { get; } = new();

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
                OnPropertyChanged(nameof(SelectedCountText));
        }
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

    /// <summary>Quand true, seuls les jeux présentant un problème (fichier ou jaquette manquants) sont affichés.</summary>
    public bool ShowIssuesOnly
    {
        get => _showIssuesOnly;
        set { if (SetProperty(ref _showIssuesOnly, value)) RefreshFilter(); }
    }

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

    /// <summary>Resynchronise manuellement la base Derby depuis RomStation.</summary>
    public ICommand SyncDbCommand { get; private set; } = null!;

    /// <summary>Ouvre la fenêtre Paramètres en modal.</summary>
    public ICommand SettingsCommand { get; private set; } = null!;

    // ── Constructeur ─────────────────────────────────────────────────────

    /// <summary>
    /// Constructeur principal — crée le VM vide.
    /// Le chargement réel des données se fait via LoadLibraryAsync()
    /// appelé depuis App.xaml.cs après l'affichage de MainWindow.
    /// </summary>
    public MainViewModel(UserPreferences preferences)
    {
        _preferences   = preferences;
        // Initialisation du mode d'affichage depuis les préférences — défaut "Mosaic"
        _isMosaicView  = preferences.LastViewMode != "List";
        // Initialisation directe sur le champ pour éviter un SaveThumbnailSizePreference inutile au démarrage
        _thumbnailSize = preferences.ThumbnailSize;
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
        {
            foreach (var g in FilteredGames) g.IsSelected = true;
            RefreshSelectedCount();
        });
        DeselectAllGamesCommand = new RelayCommand(() =>
        {
            foreach (var g in FilteredGames) g.IsSelected = false;
            RefreshSelectedCount();
        });

        SwitchToMosaicCommand = new RelayCommand(() => IsMosaicView = true);
        SwitchToListCommand   = new RelayCommand(() => IsMosaicView = false);

        RebaseCommand = new RelayCommand(
            execute: () =>
            {
                var selected = Games.Where(g => g.IsSelected).ToList();
                // ViewModel passé au constructeur pour que l'injection soit immédiate
                var vm  = new RebaseViewModel(selected, _romStationPath, _preferences);
                var win = new RebaseWindow(vm)
                {
                    Owner = Application.Current.MainWindow,
                };
                win.ShowDialog();
            },
            canExecute: () => Games.Any(g => g.IsSelected));

        SyncDbCommand = new RelayCommand(
            execute: async () => await SyncDbAsync(),
            canExecute: () => !_isLoading);

        SettingsCommand = new RelayCommand(OpenSettings);
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
    public async Task LoadLibraryAsync(string dbCopyPath, string romStationPath)
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
                Systems.Add(new SystemItemViewModel(sys.Name, sys.ImagePath, count, RefreshFilter));
            }

            var gameVms = games.Select(g => new GameItemViewModel(
                g.Id, g.Title, g.SystemName, g.SystemImagePath,
                g.CoverPath, g.CoverExists, g.FileExists,
                g.FileCount, g.GameDirectory, false, false, RefreshSelectedCount)).ToList();

            foreach (var vm in gameVms) Games.Add(vm);
            FilteredGames = new ObservableCollection<GameItemViewModel>(gameVms);

            _lastSyncDate = DateTime.Now;
            OnPropertyChanged(nameof(LastSyncText));
            OnPropertyChanged(nameof(FilteredGameCount));

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
                System.Diagnostics.Debug.WriteLine($"[SimulateProgress] step={i}/{steps} progress={progress:F1}");
            }, System.Windows.Threading.DispatcherPriority.Render);
        }
    }

    // ── Synchronisation manuelle ──────────────────────────────────────────

    /// <summary>
    /// Resynchronise la base Derby depuis RomStation sans quitter l'application.
    /// Affiche l'overlay de chargement, recopie la base, supprime db.lck de la copie,
    /// puis recharge systèmes et jeux via LoadLibraryAsync.
    /// </summary>
    public async Task SyncDbAsync()
    {
        if (_isLoading) return; // garde-fou si déjà en cours

        if (string.IsNullOrWhiteSpace(_romStationPath) || string.IsNullOrWhiteSpace(_dbCopyPath))
            return;

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
                Strings.Splash_DBLocked,
                "OK",
                null) { Owner = Application.Current.MainWindow };
            dialog.ShowDialog();
            return;
        }

        // Vider les collections existantes avant le rechargement
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            Systems.Clear();
            Games.Clear();
            FilteredGames = new ObservableCollection<GameItemViewModel>();
        });

        // Rechargement complet — IsLoading sera remis à false en fin de LoadLibraryAsync,
        // LastSyncText sera automatiquement mis à jour via OnPropertyChanged
        await LoadLibraryAsync(_dbCopyPath, _romStationPath);
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

            // Décoche les jeux dont le système vient d'être masqué
            foreach (var game in Games)
            {
                if (game.IsSelected && !checkedSystems.Contains(game.SystemName))
                    game.IsSelected = false;
            }

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
        });
    }

    /// <summary>Recalcule le nombre de jeux sélectionnés à partir de la liste complète.</summary>
    private void RefreshSelectedCount()
        => SelectedGameCount = Games.Count(g => g.IsSelected);

    /// <summary>Ouvre le panneau de paramètres en modal.</summary>
    private void OpenSettings()
    {
        var vm  = new SettingsViewModel(_preferences);
        var win = new Views.Dialogs.SettingsWindow(vm)
        {
            Owner = Application.Current.MainWindow,
        };
        win.ShowDialog();
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
}
