using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using RomStationRebase.Helpers;
using RomStationRebase.Models;
using RomStationRebase.Resources;
using RomStationRebase.Services;
using RomStationRebase.Views.Dialogs;

namespace RomStationRebase.ViewModels;

/// <summary>ViewModel de RebaseWindow — gère la configuration, l'exécution et la progression du rebase.</summary>
public class RebaseViewModel : ViewModelBase
{
    private readonly ArchitectureService     _archService   = new();
    private readonly RebaseService           _rebaseService = new();
    private readonly List<GameItemViewModel> _selectedGames;
    private readonly string                  _romStationPath;
    private readonly UserPreferences?        _preferences;
    private readonly ConfigService           _configService = new();

    private CancellationTokenSource? _cts;
    private CancellationTokenSource  _sizeCts            = new();
    private long                     _estimatedSizeBytes = -1;

    private string               _targetPath           = string.Empty;
    private ArchitectureEntry?   _selectedArchitecture;
    private bool                 _generateM3U;
    private int                  _duplicatePolicyIndex;
    private int                  _maxParallelCopies    = 4;
    private int                  _retryCount           = 2;
    private int                  _retryDelay           = 3;
    private double               _globalProgress;
    private string               _statusText           = string.Empty;
    private string               _speedText            = string.Empty;
    private string               _etaText              = string.Empty;
    private bool                 _isRunning;
    private bool                 _isPaused;
    private bool                 _isCancelling;
    private bool                 _isSizeCalculating;
    private bool                 _isSizeCalculated;
    private string               _estimatedSizeText    = string.Empty;
    private ManualResetEventSlim _pauseEvent           = new(true);

    // ── Collections ───────────────────────────────────────────────────────

    /// <summary>Architectures disponibles chargées depuis config/architectures/index.json.</summary>
    public ObservableCollection<ArchitectureEntry> Architectures { get; } = new();

    /// <summary>Jeux à traiter — peuplé dès l'ouverture, statuts mis à jour pendant le rebase.</summary>
    public ObservableCollection<RebaseGameItemViewModel> RebaseItems { get; } = new();

    // ── Propriétés de configuration ───────────────────────────────────────

    /// <summary>Chemin du dossier de destination.</summary>
    public string TargetPath
    {
        get => _targetPath;
        set
        {
            if (SetProperty(ref _targetPath, value))
                OnPropertyChanged(nameof(CanStart));
        }
    }

    /// <summary>Architecture cible sélectionnée. Null si aucune sélection.</summary>
    public ArchitectureEntry? SelectedArchitecture
    {
        get => _selectedArchitecture;
        set
        {
            if (SetProperty(ref _selectedArchitecture, value))
            {
                if (value != null)
                    GenerateM3U = value.GenerateM3UByDefault;
                OnPropertyChanged(nameof(CanStart));
            }
        }
    }

    /// <summary>Génère des fichiers M3U pour les jeux multi-disques.</summary>
    public bool GenerateM3U
    {
        get => _generateM3U;
        set => SetProperty(ref _generateM3U, value);
    }

    /// <summary>Index de la politique de doublons : 0 = Ignore, 1 = Overwrite.</summary>
    public int DuplicatePolicyIndex
    {
        get => _duplicatePolicyIndex;
        set => SetProperty(ref _duplicatePolicyIndex, value);
    }

    private DuplicatePolicy DuplicatePolicy
        => _duplicatePolicyIndex == 1 ? DuplicatePolicy.Overwrite : DuplicatePolicy.Ignore;

    /// <summary>Nombre de copies simultanées (1–16).</summary>
    public int MaxParallelCopies
    {
        get => _maxParallelCopies;
        set => SetProperty(ref _maxParallelCopies, value);
    }

    /// <summary>Nombre de tentatives en cas d'échec (0–5).</summary>
    public int RetryCount
    {
        get => _retryCount;
        set => SetProperty(ref _retryCount, value);
    }

    /// <summary>Délai en secondes entre deux tentatives (1–30).</summary>
    public int RetryDelay
    {
        get => _retryDelay;
        set => SetProperty(ref _retryDelay, value);
    }

    // ── Propriétés de progression ─────────────────────────────────────────

    /// <summary>Déclenché à chaque mise à jour de GlobalProgress — souscrit par RebaseWindow pour l'animation.</summary>
    public event Action<double>? ProgressChanged;

    /// <summary>Progression globale du rebase en pourcentage (0–100).</summary>
    public double GlobalProgress
    {
        get => _globalProgress;
        private set
        {
            if (SetProperty(ref _globalProgress, value))
            {
                Debug.WriteLine($"[Rebase] GlobalProgress: {value:F1}%");
                ProgressChanged?.Invoke(value);
            }
        }
    }

    /// <summary>Message de statut affiché sous la barre de progression.</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>Vitesse de copie formatée (ex : "25,4 MB/s").</summary>
    public string SpeedText
    {
        get => _speedText;
        private set => SetProperty(ref _speedText, value);
    }

    /// <summary>Temps restant estimé (ex : "~2 min").</summary>
    public string EtaText
    {
        get => _etaText;
        private set => SetProperty(ref _etaText, value);
    }

    // ── État ──────────────────────────────────────────────────────────────

    /// <summary>True pendant l'exécution du rebase.</summary>
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(IsNotRunning));
                OnPropertyChanged(nameof(CanShowStart));
                OnPropertyChanged(nameof(ShowPauseResume));
            }
        }
    }

    /// <summary>Inverse de IsRunning — active/désactive la zone de configuration.</summary>
    public bool IsNotRunning => !_isRunning;

    /// <summary>True si le rebase est suspendu.</summary>
    public bool IsPaused
    {
        get => _isPaused;
        private set
        {
            if (SetProperty(ref _isPaused, value))
                OnPropertyChanged(nameof(PauseResumeText));
        }
    }

    /// <summary>True pendant l'attente de l'arrêt effectif des tâches après annulation.</summary>
    public bool IsCancelling
    {
        get => _isCancelling;
        private set
        {
            if (SetProperty(ref _isCancelling, value))
                OnPropertyChanged(nameof(ShowPauseResume));
        }
    }

    /// <summary>True si les boutons Pause/Reprendre et Annuler doivent être visibles (rebase actif et pas en cours d'annulation).</summary>
    public bool ShowPauseResume => _isRunning && !_isCancelling;

    /// <summary>Libellé du bouton Pause/Reprendre selon l'état courant.</summary>
    public string PauseResumeText
        => _isPaused ? Strings.Rebase_Resume : Strings.Rebase_Pause;

    /// <summary>True si le rebase peut démarrer.</summary>
    public bool CanStart
        => !_isRunning
        && !_isSizeCalculating
        && !string.IsNullOrWhiteSpace(_targetPath)
        && _selectedArchitecture != null;

    /// <summary>True pendant le calcul de la taille estimée.</summary>
    public bool IsSizeCalculating
    {
        get => _isSizeCalculating;
        private set
        {
            if (SetProperty(ref _isSizeCalculating, value))
            {
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(CanShowStart));
            }
        }
    }

    /// <summary>True quand le calcul de taille s'est terminé avec succès.</summary>
    public bool IsSizeCalculated
    {
        get => _isSizeCalculated;
        private set => SetProperty(ref _isSizeCalculated, value);
    }

    /// <summary>Taille estimée formatée (ex : "12.3 GB") ou message d'annulation.</summary>
    public string EstimatedSizeText
    {
        get => _estimatedSizeText;
        private set => SetProperty(ref _estimatedSizeText, value);
    }

    /// <summary>True quand le bouton Démarrer doit être visible (pas de rebase ni de calcul en cours).</summary>
    public bool CanShowStart => !_isRunning && !_isSizeCalculating;

    /// <summary>Fenêtre propriétaire — définie par RebaseWindow.xaml.cs pour les ConfirmDialog.</summary>
    public Window? OwnerWindow { get; set; }

    /// <summary>
    /// Callback de confirmation avant annulation du rebase — injecté depuis RebaseWindow.xaml.cs
    /// pour éviter de coupler le ViewModel à la View. Retourne true si l'utilisateur confirme.
    /// </summary>
    public Func<bool>? ConfirmCancel { get; set; }

    // ── Commandes ─────────────────────────────────────────────────────────

    public ICommand BrowseCommand               { get; }
    public ICommand StartRebaseCommand          { get; }
    public ICommand PauseResumeCommand          { get; }
    public ICommand CancelCommand               { get; }
    public ICommand CancelCalculationCommand    { get; }
    public ICommand OpenFolderCommand           { get; }
    public ICommand ExportLogCommand            { get; }

    /// <summary>Décrémente MaxParallelCopies (minimum 1).</summary>
    public ICommand DecrementParallelCommand    { get; }

    /// <summary>Incrémente MaxParallelCopies (maximum 16).</summary>
    public ICommand IncrementParallelCommand    { get; }

    public ICommand DecrementRetryCommand       { get; }
    public ICommand IncrementRetryCommand       { get; }
    public ICommand DecrementRetryDelayCommand  { get; }
    public ICommand IncrementRetryDelayCommand  { get; }

    // ── Constructeur ─────────────────────────────────────────────────────

    /// <summary>
    /// Initialise le ViewModel, charge les architectures et pré-remplit RebaseItems
    /// avec les jeux sélectionnés en statut "Prêt" pour affichage immédiat.
    /// </summary>
    public RebaseViewModel(List<GameItemViewModel> selectedGames, string romStationPath, UserPreferences? preferences = null)
    {
        _selectedGames  = selectedGames;
        _romStationPath = romStationPath;

        // Initialisation depuis les préférences utilisateur (ou valeurs par défaut si null)
        _preferences = preferences;
        if (preferences != null)
        {
            _targetPath           = preferences.LastRebaseTargetPath;
            _generateM3U          = preferences.LastRebaseGenerateM3U;
            _duplicatePolicyIndex = preferences.DuplicatePolicy == "Overwrite" ? 1 : 0;
            _maxParallelCopies    = Math.Clamp(preferences.MaxParallelCopies, 1, 16);
            _retryCount           = Math.Clamp(preferences.RetryCount, 0, 5);
            _retryDelay           = Math.Clamp(preferences.RetryDelaySeconds, 1, 30);
            // _selectedArchitecture sera restaurée après LoadArchitectures() ci-dessous
        }

        BrowseCommand               = new RelayCommand(OnBrowse);
        StartRebaseCommand          = new RelayCommand(async () => await OnStartRebaseAsync(), () => CanStart);
        PauseResumeCommand          = new RelayCommand(OnPauseResume,         () => _isRunning);
        CancelCommand               = new RelayCommand(OnCancel,              () => _isRunning && !_isCancelling);
        CancelCalculationCommand    = new RelayCommand(OnCancelCalculation,   () => _isSizeCalculating);
        OpenFolderCommand           = new RelayCommand(OnOpenFolder,          () => !string.IsNullOrWhiteSpace(_targetPath));
        ExportLogCommand            = new RelayCommand(OnExportLog,           () => RebaseItems.Count > 0);
        DecrementParallelCommand    = new RelayCommand(() => MaxParallelCopies--, () => _maxParallelCopies > 1);
        IncrementParallelCommand    = new RelayCommand(() => MaxParallelCopies++, () => _maxParallelCopies < 16);
        DecrementRetryCommand       = new RelayCommand(() => RetryCount--,        () => _retryCount > 0);
        IncrementRetryCommand       = new RelayCommand(() => RetryCount++,        () => _retryCount < 5);
        DecrementRetryDelayCommand  = new RelayCommand(() => RetryDelay--,        () => _retryDelay > 1);
        IncrementRetryDelayCommand  = new RelayCommand(() => RetryDelay++,        () => _retryDelay < 30);

        LoadArchitectures();

        // Restaurer la dernière architecture sélectionnée, sinon garder celle marquée IsDefault (logique existante dans LoadArchitectures)
        if (preferences != null && !string.IsNullOrWhiteSpace(preferences.LastRebaseArchitectureId))
        {
            var match = Architectures.FirstOrDefault(a => a.Id == preferences.LastRebaseArchitectureId);
            if (match != null)
            {
                SelectedArchitecture = match;
                // Réappliquer GenerateM3U depuis les préférences — le setter de SelectedArchitecture
                // l'a écrasé avec GenerateM3UByDefault de l'architecture
                GenerateM3U = preferences.LastRebaseGenerateM3U;
            }
        }

        PopulateRebaseItems();
        StatusText = string.Format(Strings.Rebase_Ready, selectedGames.Count);

        // Calcul automatique de la taille dès l'ouverture — résultat disponible avant le clic sur Démarrer
        _ = CalculateSizesAsync();
    }

    // ── Chargement ────────────────────────────────────────────────────────

    /// <summary>Charge les architectures et sélectionne celle marquée IsDefault.</summary>
    private void LoadArchitectures()
    {
        try
        {
            var list = _archService.LoadArchitectures();
            foreach (var arch in list)
                Architectures.Add(arch);
            SelectedArchitecture = Architectures.FirstOrDefault(a => a.IsDefault) ?? Architectures.FirstOrDefault();
        }
        catch (Exception ex)
        {
            StatusText = $"Erreur chargement architectures : {ex.Message}";
        }
    }

    /// <summary>Pré-remplit RebaseItems depuis _selectedGames avec Status = Pending.</summary>
    private void PopulateRebaseItems()
    {
        RebaseItems.Clear();
        foreach (var g in _selectedGames)
        {
            RebaseItems.Add(new RebaseGameItemViewModel
            {
                GameId          = g.Id,
                Title           = g.Title,
                SystemName      = g.SystemName,
                SystemImagePath = g.SystemImagePath,
                CoverPath       = g.CoverPath,
                CoverExists     = g.CoverExists,
                FileCount       = g.FileCount,
            });
        }
    }

    // ── Exécution ─────────────────────────────────────────────────────────

    /// <summary>Valide la configuration puis lance RunRebaseAsync en arrière-plan.</summary>
    private async Task OnStartRebaseAsync()
    {
        // a. Dossier cible vide
        if (string.IsNullOrWhiteSpace(_targetPath))
        {
            ShowConfirm(Strings.Rebase_Title, Strings.Rebase_Validation_NoTarget, "OK");
            return;
        }

        // a-bis. Le chemin doit être absolu (rejette les chemins relatifs et les lettres de lecteur invalides)
        if (!Path.IsPathFullyQualified(_targetPath))
        {
            ShowConfirm(Strings.Rebase_Title, Strings.Rebase_Validation_PathNotAbsolute, "OK");
            return;
        }

        // b. Dossier cible inexistant
        if (!Directory.Exists(_targetPath))
        {
            // b-1. Vérifier que la racine du lecteur existe avant de proposer la création
            string? driveRoot = Path.GetPathRoot(_targetPath);
            if (string.IsNullOrEmpty(driveRoot) || !Directory.Exists(driveRoot))
            {
                ShowConfirm(Strings.Rebase_Title, Strings.Rebase_Error_InvalidDriveLetter, "OK");
                return;
            }

            // b-2. Racine OK mais sous-dossier inexistant → proposer de créer
            var dlg = ShowConfirm(Strings.Rebase_Title, Strings.Rebase_FolderNotExist,
                Strings.Rebase_FolderCreate, Strings.General_Cancel);
            if (!dlg.Result) return;
            try   { Directory.CreateDirectory(_targetPath); }
            catch (Exception ex)
            {
                string userMessage = Helpers.ErrorMessageClassifier.Classify(ex);
                ShowConfirm(Strings.Rebase_Title, userMessage, "OK");
                return;
            }
        }
        // c. Dossier cible non vide → demander confirmation (en ignorant les fichiers/dossiers système Windows)
        else if (ContainsVisibleEntries(_targetPath))
        {
            var dlg = ShowConfirm(Strings.Rebase_Title, Strings.Rebase_FolderNotEmpty,
                Strings.General_Yes, Strings.General_Cancel);
            if (!dlg.Result) return;
        }

        if (_selectedArchitecture is null)
        {
            ShowConfirm(Strings.Rebase_Title, Strings.Rebase_Validation_NoArchitecture, "OK");
            return;
        }
        if (_selectedGames.Count == 0)
        {
            ShowConfirm(Strings.Rebase_Title, Strings.Rebase_Validation_NoGames, "OK");
            return;
        }

        FolderTreeMapping mapping;
        try
        {
            mapping = _archService.LoadFolderTreeMapping(_selectedArchitecture.FolderTreeMapping);
        }
        catch (Exception ex)
        {
            ShowConfirm(Strings.Rebase_Title, ex.Message, "OK");
            return;
        }

        // Avertissement systèmes sans mapping
        var unmapped = _archService.GetUnmappedSystems(mapping, _selectedGames);
        if (unmapped.Count > 0)
        {
            string msg = string.Format(Strings.Rebase_Validation_UnmappedSystems,
                unmapped.Count, string.Join(", ", unmapped));
            var dlg = ShowConfirm(Strings.Rebase_Title, msg, Strings.General_Yes, Strings.General_Cancel);
            if (!dlg.Result) return;
        }

        // Utiliser la taille déjà calculée au démarrage ; recalculer si elle était annulée
        long estimatedSize;
        if (IsSizeCalculated)
        {
            estimatedSize = _estimatedSizeBytes;
        }
        else
        {
            estimatedSize = await CalculateSizesAsync();
            if (estimatedSize < 0) return; // calcul annulé par l'utilisateur
        }

        if (!_archService.CheckDiskSpace(_targetPath, estimatedSize))
        {
            double gb = estimatedSize / 1_073_741_824.0;
            ShowConfirm(Strings.Rebase_Title,
                string.Format(Strings.Rebase_Validation_NoSpace, gb.ToString("F1")), "OK");
            return;
        }

        // Réinitialisation des statuts — les items sont déjà affichés depuis l'ouverture
        var itemMap = new Dictionary<int, RebaseGameItemViewModel>();
        foreach (var item in RebaseItems)
        {
            item.Status      = RebaseItemStatus.Pending;
            item.Progress    = 0;
            item.ErrorDetail = null;
            itemMap[item.GameId] = item;
        }

        // Persistance des paramètres de rebase — on sauvegarde uniquement à ce stade,
        // quand toutes les validations sont passées et que le rebase va réellement démarrer.
        SaveRebasePreferences();

        _pauseEvent    = new ManualResetEventSlim(true);
        _cts           = new CancellationTokenSource();
        IsRunning      = true;
        IsPaused       = false;
        GlobalProgress = 1;

        var options = new RebaseOptions
        {
            SelectedGames     = _selectedGames,
            TargetPath        = _targetPath,
            Architecture      = _selectedArchitecture,
            Mapping           = mapping,
            GenerateM3U       = _generateM3U,
            DuplicatePolicy   = DuplicatePolicy,
            MaxParallelCopies = _maxParallelCopies,
            RetryCount        = _retryCount,
            RetryDelaySeconds = _retryDelay,
            RomStationPath    = _romStationPath,
            PauseEvent        = _pauseEvent,
        };

        var progress = new Progress<RebaseProgress>(OnProgressChanged(itemMap));

        // Drapeaux capturés dans le finally pour piloter l'affichage post-rebase
        bool       wasCancelled = false;
        Exception? fatalError   = null;

        try
        {
            await _rebaseService.RunRebaseAsync(options, progress, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            // L'utilisateur a cliqué Annuler — pas de popup, il sait ce qu'il a fait
            wasCancelled = true;
        }
        catch (Exception ex)
        {
            // Erreur fatale non attendue — on la stocke pour l'afficher après le finally
            fatalError = ex;
        }
        finally
        {
            IsRunning    = false;
            IsPaused     = false;
            IsCancelling = false;

            // Reset visuel de la barre : 0 si interrompu, 100 si terminé normalement
            GlobalProgress = (wasCancelled || fatalError != null) ? 0 : 100;

            // En cas d'erreur fatale, les items encore en "Copie en cours" au moment de l'exception
            // restent figés dans cet état s'ils ne sont pas corrigés ici. On les bascule en Failed
            // avec le message d'erreur classifié pour la colonne Erreur du DataGrid.
            if (fatalError != null)
            {
                string itemErrorMessage = ErrorMessageClassifier.Classify(fatalError);
                foreach (var item in RebaseItems.Where(i => i.Status == RebaseItemStatus.Copying))
                {
                    item.Status      = RebaseItemStatus.Failed;
                    item.ErrorDetail = itemErrorMessage;
                }
            }
        }

        // ── Affichage des popups de fin — après le finally pour ne pas interférer avec le reset d'état ──

        if (wasCancelled)
        {
            // Pas de popup — l'utilisateur a annulé volontairement
        }
        else if (fatalError != null)
        {
            // Message classifié pour les cas courants, fallback sur le message brut sinon
            string userMessage = ErrorMessageClassifier.Classify(fatalError);
            ShowConfirm(
                Strings.Rebase_Error_Title,
                string.Format(Strings.Rebase_Error_Message, userMessage),
                "OK");
        }
        else
        {
            // Fin normale : déterminer succès complet ou partiel depuis les statuts des items
            int completed = RebaseItems.Count(i => i.Status == RebaseItemStatus.Done);
            int failed    = RebaseItems.Count(i => i.Status == RebaseItemStatus.Failed);
            int skipped   = RebaseItems.Count(i => i.Status == RebaseItemStatus.Skipped);

            if (failed == 0 && skipped == 0)
            {
                ShowConfirm(
                    Strings.Rebase_Completed_Title,
                    string.Format(Strings.Rebase_Completed_Success, completed),
                    "OK");
            }
            else
            {
                ShowConfirm(
                    Strings.Rebase_Completed_Partial_Title,
                    string.Format(Strings.Rebase_Completed_Partial, completed, failed, skipped),
                    "OK");
            }
        }
    }

    /// <summary>Callback IProgress — met à jour les stats globales et l'item courant sur le thread UI.</summary>
    private Action<RebaseProgress> OnProgressChanged(Dictionary<int, RebaseGameItemViewModel> itemMap)
        => p =>
        {
            GlobalProgress = p.TotalBytes > 0
                ? (double)p.CopiedBytes / p.TotalBytes * 100
                : 0;

            SpeedText  = FormatSpeed(p.SpeedBytesPerSecond);
            EtaText    = FormatEta(p.EstimatedTimeRemaining);
            StatusText = string.Format(Strings.Rebase_GamesCount,
                p.CompletedFiles + p.FailedFiles + p.SkippedFiles, p.TotalFiles);

            if (p.CompletedFiles + p.FailedFiles + p.SkippedFiles == p.TotalFiles && p.TotalFiles > 0)
            {
                StatusText     = string.Format(Strings.Rebase_Completed,
                    p.CompletedFiles, p.FailedFiles, p.SkippedFiles);
                GlobalProgress = 100;
                SpeedText      = string.Empty;
                EtaText        = string.Empty;
            }

            if (p.CurrentItem is not null && itemMap.TryGetValue(p.CurrentItem.GameId, out var vm))
            {
                vm.Status      = p.CurrentItem.Status;
                vm.Progress    = p.CurrentItem.Progress;
                vm.ErrorDetail = p.CurrentItem.ErrorDetail;
            }
        };

    // ── Commandes ─────────────────────────────────────────────────────────

    private void OnBrowse()
    {
        var dialog = new OpenFolderDialog { Title = Strings.Rebase_TargetPath };

        // Priorité au chemin courant s'il est accessible (Directory.Exists
        // retourne false silencieusement sur lecteur déconnecté, ce qui
        // évite l'exception ShowDialog()). Sinon, on impose "Mes documents"
        // comme point de départ neutre et prévisible, plutôt que le cache
        // shell Windows qui peut pointer vers n'importe quel dossier
        // ouvert récemment dans n'importe quelle application.
        if (!string.IsNullOrWhiteSpace(_targetPath) && Directory.Exists(_targetPath))
            dialog.InitialDirectory = _targetPath;
        else
            dialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        if (dialog.ShowDialog() == true)
            TargetPath = dialog.FolderName;
    }

    private void OnPauseResume()
    {
        if (_isPaused) { _pauseEvent.Set();   IsPaused = false; }
        else           { _pauseEvent.Reset(); IsPaused = true;  }
    }

    private void OnCancel()
    {
        // Garde-fou : si le rebase s'est terminé entre l'activation du bouton et ce clic,
        // on ne déclenche ni popup ni flag IsCancelling.
        if (!IsRunning) return;

        if (ConfirmCancel?.Invoke() == true)
        {
            // Re-vérification après le popup — l'utilisateur a pu mettre du temps à répondre.
            if (!IsRunning) return;

            IsCancelling = true;
            StopRebase();
        }
    }

    /// <summary>Annule le rebase sans confirmation — utilisé depuis OnClosing qui a son propre dialog.</summary>
    internal void StopRebase()
    {
        _cts?.Cancel();
        _pauseEvent.Set(); // débloquer les tâches en pause pour qu'elles voient l'annulation
    }

    private void OnCancelCalculation()
    {
        var dlg = ShowConfirm(
            Strings.Rebase_CancelCalcTitle,
            Strings.Rebase_CancelCalcMessage,
            Strings.General_Yes,
            Strings.General_Cancel);
        if (dlg.Result)
            _sizeCts.Cancel();
    }

    /// <summary>Annule le calcul de taille sans confirmation — appelé depuis OnClosing qui gère son propre dialog.</summary>
    internal void StopSizeCalculation() => _sizeCts.Cancel();

    /// <summary>
    /// Calcule la taille totale des fichiers à copier en itérant sur les jeux sélectionnés.
    /// Vérifie l'annulation entre chaque jeu. Retourne -1 si le calcul est annulé.
    /// </summary>
    private async Task<long> CalculateSizesAsync()
    {
        _sizeCts         = new CancellationTokenSource();
        IsSizeCalculating = true;
        IsSizeCalculated  = false;
        EstimatedSizeText = string.Empty;

        try
        {
            long total = await Task.Run(() =>
            {
                long sum   = 0;
                int  count = _selectedGames.Count;

                for (int i = 0; i < count; i++)
                {
                    _sizeCts.Token.ThrowIfCancellationRequested();

                    var    game     = _selectedGames[i];
                    if (string.IsNullOrEmpty(game.GameDirectory)) continue;

                    string gameRoot = ArchitectureService.ResolveGameRoot(game.GameDirectory, _romStationPath);
                    if (!Directory.Exists(gameRoot)) continue;

                    var files = Directory
                        .GetFiles(gameRoot, "*", SearchOption.AllDirectories)
                        .Where(f => !f.Contains(Path.DirectorySeparatorChar + "images" + Path.DirectorySeparatorChar));
                    sum += files.Sum(f => { try { return new FileInfo(f).Length; } catch { return 0L; } });
                }
                return sum;
            }, _sizeCts.Token);

            _estimatedSizeBytes = total;
            EstimatedSizeText   = FormatSize(total);
            IsSizeCalculated    = true;
            return total;
        }
        catch (OperationCanceledException)
        {
            _estimatedSizeBytes = -1;
            EstimatedSizeText   = Strings.Rebase_CalculationCancelled;
            IsSizeCalculated    = false;
            return -1;
        }
        finally
        {
            IsSizeCalculating = false;
        }
    }

    private void OnOpenFolder()
    {
        if (!string.IsNullOrWhiteSpace(_targetPath) && Directory.Exists(_targetPath))
        {
            Process.Start("explorer.exe", _targetPath);
            return;
        }

        // Le chemin est inaccessible. On distingue deux causes :
        //   1. Le lecteur (racine du chemin) n'existe pas → message "lettre de lecteur invalide"
        //   2. Le lecteur existe mais le dossier spécifié n'existe pas → message "dossier introuvable"
        string? root = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(_targetPath))
                root = Path.GetPathRoot(_targetPath);
        }
        catch
        {
            root = null;
        }

        bool rootExists = !string.IsNullOrWhiteSpace(root) && Directory.Exists(root);

        string message = rootExists
            ? Strings.Rebase_Error_FolderNotExist
            : Strings.Rebase_Error_InvalidDriveLetter;

        ShowConfirm(Strings.Rebase_Title, message, "OK");
    }

    private void OnExportLog()
    {
        if (RebaseItems.Count == 0) return;
        var dialog = new SaveFileDialog
        {
            Title      = Strings.Rebase_ExportLog,
            Filter     = "CSV (*.csv)|*.csv|Texte (*.txt)|*.txt",
            DefaultExt = "csv",
            FileName   = $"rebase-log-{DateTime.Now:yyyyMMdd-HHmm}.csv",
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            using var w = new StreamWriter(dialog.FileName, false, System.Text.Encoding.UTF8);
            w.WriteLine("Titre;Système;Fichiers;Statut;Erreur");
            foreach (var item in RebaseItems)
                w.WriteLine($"{item.Title};{item.SystemName};{item.FileCount};{item.StatusText};{item.ErrorDetail?.Replace(";", ",")}");
        }
        catch (Exception ex)
        {
            ShowConfirm(Strings.Rebase_ExportLog, ex.Message, "OK");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Ouvre un ConfirmDialog modal avec OwnerWindow comme propriétaire.
    /// Si OwnerWindow est null, la dialog se centre sur l'écran.
    /// </summary>
    private ConfirmDialog ShowConfirm(string title, string message, string primary, string? secondary = null)
    {
        var dlg = new ConfirmDialog(title, message, primary, secondary)
        {
            Owner = OwnerWindow,
        };
        dlg.ShowDialog();
        return dlg;
    }

    /// <summary>
    /// Sauvegarde les paramètres de rebase courants dans UserPreferences.
    /// Appelée au démarrage effectif du rebase (sans bounds) ou à la fermeture
    /// de la fenêtre (avec bounds capturées par le code-behind).
    /// Silencieux en cas d'échec d'écriture — ne bloque pas le lancement du rebase.
    /// </summary>
    internal void SaveRebasePreferences(Models.WindowBounds? bounds = null)
    {
        if (_preferences == null) return;

        try
        {
            if (!string.IsNullOrWhiteSpace(_targetPath))
                _preferences.LastRebaseTargetPath = _targetPath;
            _preferences.LastRebaseArchitectureId = _selectedArchitecture?.Id ?? string.Empty;
            _preferences.LastRebaseGenerateM3U    = _generateM3U;
            _preferences.DuplicatePolicy          = _duplicatePolicyIndex == 1 ? "Overwrite" : "Ignore";
            _preferences.MaxParallelCopies        = _maxParallelCopies;
            _preferences.RetryCount               = _retryCount;
            _preferences.RetryDelaySeconds        = _retryDelay;

            // Les bounds ne sont fusionnés que lorsque l'appelant les fournit
            // (typiquement OnClosing). Au lancement effectif du rebase, l'appelant
            // ne passe pas de bounds : la propriété existante est préservée.
            if (bounds != null)
                _preferences.RebaseWindowBounds = bounds;

            _configService.SaveUserPreferences(_preferences);
        }
        catch
        {
            // Ne pas bloquer le rebase si la sauvegarde échoue
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F1} GB";
        if (bytes >= 1_048_576)     return $"{bytes / 1_048_576.0:F1} MB";
        if (bytes >= 1024)          return $"{bytes / 1024.0:F0} KB";
        return $"{bytes} B";
    }

    private static string FormatSpeed(double bytesPerSecond)
    {
        if (bytesPerSecond <= 0)         return string.Empty;
        if (bytesPerSecond >= 1_073_741_824) return $"{bytesPerSecond / 1_073_741_824.0:F1} GB/s";
        if (bytesPerSecond >= 1_048_576)     return $"{bytesPerSecond / 1_048_576.0:F1} MB/s";
        return $"{bytesPerSecond / 1024.0:F0} KB/s";
    }

    private static string FormatEta(TimeSpan eta)
    {
        if (eta <= TimeSpan.Zero)     return string.Empty;
        if (eta.TotalMinutes >= 60)   return $"~{(int)eta.TotalHours}h{eta.Minutes:D2}";
        if (eta.TotalSeconds >= 60)   return $"~{(int)eta.TotalMinutes} min";
        return $"~{(int)eta.TotalSeconds} s";
    }

    /// <summary>
    /// Retourne true si le dossier contient au moins une entrée visible (fichier ou dossier).
    /// Ignore les éléments avec attributs Hidden ou System — notamment "System Volume Information"
    /// et "$RECYCLE.BIN" créés automatiquement par Windows sur les supports amovibles.
    /// </summary>
    private static bool ContainsVisibleEntries(string path)
    {
        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(path))
            {
                var attrs = File.GetAttributes(entry);
                // Un élément est "visible" s'il n'est ni caché ni système
                if ((attrs & FileAttributes.Hidden) == 0 && (attrs & FileAttributes.System) == 0)
                    return true;
            }
            return false;
        }
        catch
        {
            // En cas d'erreur d'accès, on considère le dossier comme potentiellement non vide
            // pour garder le comportement prudent (demander confirmation)
            return true;
        }
    }
}
