using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
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

/// <summary>ViewModel de RebaseWindow — gère la configuration, le plan, l'exécution et la progression du rebase.</summary>
public class RebaseViewModel : ViewModelBase
{
    private readonly ArchitectureService     _archService   = new();
    private readonly RebaseService           _rebaseService = new();
    private readonly RebasePlanner           _planner       = new();
    private readonly ArchiveInspector        _inspector     = new();
    private readonly DerbyService            _derby         = new();
    private readonly List<GameItemViewModel> _selectedGames;
    private readonly List<GameItemViewModel> _allGames;
    private readonly string                  _romStationPath;
    private readonly string                  _dbCopyPath;
    private readonly UserPreferences?        _preferences;
    private readonly ConfigService           _configService = new();
    private readonly ExternalToolService     _toolService   = new();

    // Outils externes de conversion, et l'exécutable choisi par l'utilisateur pour chacun (null : non indiqué)
    private List<ExternalTool> _tools = [];
    private Dictionary<string, string> _toolExecutables = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Sélection de travail partagée avec la fenêtre principale : paramètres et règles par jeu y sont lus à l'ouverture, rendus à la fermeture.</summary>
    private readonly RebasePresetSessionViewModel? _session;

    // Caches de l'analyse : fichiers Derby, tailles, arborescences — remplis une fois, réutilisés à chaque replanification
    private readonly Dictionary<string, FolderTreeMapping> _mappings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long>              _fileSizes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<(string RelativePath, long Size)>> _dirFiles = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<int, IReadOnlyList<GameFileInfo>> _filesByGame = new Dictionary<int, IReadOnlyList<GameFileInfo>>();
    private bool        _analysisDone;
    private RebasePlan? _plan;

    private CancellationTokenSource? _cts;
    private CancellationTokenSource  _sizeCts            = new();
    private long                     _estimatedSizeBytes = -1;

    private string               _targetPath           = string.Empty;
    private ArchitectureEntry?   _selectedArchitecture;
    private int                  _archiveModeIndex;
    private int                  _extractLayoutIndex;
    private bool                 _copyCovers           = true;
    private bool                 _generateGamelist     = true;
    private bool                 _backupGamelist;
    private bool                 _convertFiles         = true;
    private int                  _metadataLanguageIndex;
    private bool                 _metadataLanguageTouched;
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
    private bool                 _suspendReplan;

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

    /// <summary>Architecture cible sélectionnée. Null si aucune sélection. Réapplique les défauts de sortie de l'architecture.</summary>
    public ArchitectureEntry? SelectedArchitecture
    {
        get => _selectedArchitecture;
        set
        {
            if (SetProperty(ref _selectedArchitecture, value))
            {
                if (value != null)
                    ApplyArchitectureDefaults(value);
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(SupportsGamelist));
                OnPropertyChanged(nameof(ShowMetadataLanguage));
                OnPropertyChanged(nameof(GenerateGamelistLabel));
                OnPropertyChanged(nameof(MetadataLocationHint));
                RebuildPlan();
            }
        }
    }

    /// <summary>Libellé de l'interrupteur : le nom du fichier que lit la cible ("Générer miyoogamelist.xml").</summary>
    public string GenerateGamelistLabel
        => string.Format(Strings.Rebase_GenerateMetadataFile,
            MetadataFormats.DisplayFileName(_selectedArchitecture?.GamelistFormat));

    /// <summary>
    /// Rappel affiché quand les métadonnées sortent de l'arborescence des ROMs (ES-DE, Cocoon) :
    /// l'utilisateur doit savoir qu'un dossier ES-DE apparaît à la racine de la destination, et quoi en faire.
    /// </summary>
    public string? MetadataLocationHint
        => MetadataFormats.Normalize(_selectedArchitecture?.GamelistFormat) == MetadataFormats.EsDe
            ? Strings.Rebase_MetadataHint_EsDe
            : null;

    /// <summary>Traitement des archives : 0 = extraire selon l'architecture, 1 = ne jamais extraire, 2 = tout extraire.</summary>
    public int ArchiveModeIndex
    {
        get => _archiveModeIndex;
        set
        {
            if (SetProperty(ref _archiveModeIndex, value))
            {
                OnPropertyChanged(nameof(IsExtractionEnabled));
                OnPropertyChanged(nameof(IsExtractionAsConfigured));
                RebuildPlan();
            }
        }
    }

    /// <summary>True si une extraction est possible — active le choix du rangement.</summary>
    public bool IsExtractionEnabled => _archiveModeIndex != 1;

    /// <summary>True en mode « selon l'architecture » — seul mode où la colonne Extraction du tableau a prise.</summary>
    public bool IsExtractionAsConfigured => _archiveModeIndex == 0;

    /// <summary>Rangement des fichiers extraits : 0 = automatique, 1 = sous-dossier par jeu.</summary>
    public int ExtractLayoutIndex
    {
        get => _extractLayoutIndex;
        set { if (SetProperty(ref _extractLayoutIndex, value)) RebuildPlan(); }
    }

    /// <summary>Copie les jaquettes dans le dossier images de chaque système.</summary>
    public bool CopyCovers
    {
        get => _copyCovers;
        set { if (SetProperty(ref _copyCovers, value)) RebuildPlan(); }
    }

    /// <summary>Écrit un gamelist.xml par dossier système.</summary>
    public bool GenerateGamelist
    {
        get => _generateGamelist;
        set
        {
            if (SetProperty(ref _generateGamelist, value))
            {
                OnPropertyChanged(nameof(ShowMetadataLanguage));
                RebuildPlan();
            }
        }
    }

    /// <summary>Copie un gamelist.xml existant en gamelist.xml.yyyyMMdd avant de le fusionner.</summary>
    public bool BackupGamelist
    {
        get => _backupGamelist;
        set => SetProperty(ref _backupGamelist, value);
    }

    // ── Conversion par outil externe ──────────────────────────────────────

    /// <summary>Convertir les fichiers avec les outils que désigne l'architecture (GDI → CHD…), pour ce passage.</summary>
    public bool ConvertFiles
    {
        get => _convertFiles;
        set { if (SetProperty(ref _convertFiles, value)) RebuildPlan(); }
    }

    // L'interrupteur et la colonne Conversion sont toujours visibles (demande du dev) : sans outil réglé, chaque liste
    // ne propose que « Aucune », et son infobulle renvoie vers « Gérer les outils externes… ».

    /// <summary>Outils utilisables pour un jeu : ceux dont l'exécutable est indiqué et qui acceptent ce que contient le jeu. « Aucune » en tête.</summary>
    private IReadOnlyList<ToolOption> ToolOptionsFor(RebaseGamePlan? plan)
    {
        var options = new List<ToolOption> { ToolOption.None };
        if (plan is null || plan.ToolInputExtensions.Count == 0) return options;
        options.AddRange(_tools
            .Where(t => _toolExecutables.ContainsKey(t.Id) && plan.ToolInputExtensions.Any(t.Accepts))
            .Select(t => new ToolOption(t.Id, t.Label)));
        return options;
    }

    /// <summary>Outils que l'architecture demande mais dont l'exécutable n'a pas été indiqué. Null : rien à signaler (ligne masquée).</summary>
    public string? MissingToolsHint
        => _convertFiles && _plan is { UnavailableTools.Count: > 0 }
            ? string.Format(Strings.Rebase_MissingTools, string.Join(", ", _plan.UnavailableTools))
            : null;

    /// <summary>Ouvre la fenêtre des outils externes (modal) — injecté depuis la View.</summary>
    public Action? OpenExternalTools { get; set; }

    public ICommand EditToolsCommand { get; private set; } = null!;

    private void OnEditTools()
    {
        if (OpenExternalTools is null) return;
        OpenExternalTools();
        if (_preferences is not null) _configService.AdoptChildWindowBounds(_preferences);
        LoadTools();               // un exécutable vient peut-être d'être indiqué
        RebuildPlan();
    }

    private void LoadTools()
    {
        try   { _tools = _toolService.LoadTools(); }
        catch { _tools = []; }

        // Exécutables résolus une fois : le plan est recalculé à chaque option, sans accès disque
        _toolExecutables.Clear();
        try
        {
            var paths = _toolService.LoadExecutablePaths();
            foreach (var tool in _tools)
                if (ExternalToolService.ResolveExecutable(tool, paths) is { } exe)
                    _toolExecutables[tool.Id] = exe;
        }
        catch { /* index illisible : aucun outil disponible, les conversions sont simplement écartées */ }
    }

    private PlanTool? LookupTool(string id)
    {
        var tool = _tools.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
        return tool is null
            ? null
            : new PlanTool(tool.Id, tool.Label, tool.InputExtensions, tool.OutputExtension, tool.SizeRatioHint,
                           _toolExecutables.ContainsKey(tool.Id));
    }

    /// <summary>True si l'architecture sélectionnée lit un fichier de métadonnées.</summary>
    public bool SupportsGamelist => _selectedArchitecture?.SupportsGamelist == true;

    /// <summary>True si le choix de la langue des métadonnées a un sens (gamelist activé et pris en charge).</summary>
    public bool ShowMetadataLanguage => SupportsGamelist && _generateGamelist;

    /// <summary>Langue des métadonnées : 0 = français, 1 = anglais.</summary>
    public int MetadataLanguageIndex
    {
        get => _metadataLanguageIndex;
        set
        {
            if (SetProperty(ref _metadataLanguageIndex, value))
                _metadataLanguageTouched = true;
        }
    }

    private string MetadataLocale => _metadataLanguageIndex == 0 ? "fr" : "en";

    private ArchiveMode ArchiveMode => _archiveModeIndex switch
    {
        1 => ArchiveMode.Copy,
        2 => ArchiveMode.ExtractAll,
        _ => ArchiveMode.ExtractRequired,
    };

    private ExtractLayout ExtractLayout
        => _extractLayoutIndex == 1 ? ExtractLayout.Subfolder : ExtractLayout.Auto;

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
                ProgressChanged?.Invoke(value);
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
        && _selectedArchitecture != null
        && RebaseItems.Count > 0;

    /// <summary>True pendant l'analyse des fichiers et le calcul de la taille estimée.</summary>
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

    /// <summary>
    /// Ouvre l'éditeur d'architectures (modal) — injecté depuis RebaseWindow.xaml.cs.
    /// Au retour, les architectures sont rechargées et la sélection conservée.
    /// </summary>
    public Action? OpenArchitectureEditor { get; set; }

    /// <summary>Tous les systèmes RomStation de la base — proposés dans la colonne Système de l'éditeur d'architectures.</summary>
    public IReadOnlyList<string> SystemNames { get; }

    /// <summary>True si aucune architecture cible n'est disponible : le rebase ne peut pas démarrer.</summary>
    public bool HasNoArchitecture => Architectures.Count == 0;

    /// <summary>
    /// Avertissements d'ouverture, appelés par la View une fois la fenêtre affichée (un dialog a besoin d'un Owner visible) :
    /// aucune architecture cible disponible.
    /// </summary>
    public void ShowOpeningWarnings()
    {
        if (HasNoArchitecture)
            ShowConfirm(Strings.Rebase_NoArchitecture_Title, Strings.Rebase_NoArchitecture_Message, "OK");
    }

    // ── Commandes ─────────────────────────────────────────────────────────

    /// <summary>Retire un jeu de ce rebase et le décoche dans la bibliothèque. Paramètre : RebaseGameItemViewModel.</summary>
    public ICommand RemoveItemCommand           { get; }

    /// <summary>Ouvre l'éditeur d'architectures puis recharge la liste.</summary>
    public ICommand EditArchitecturesCommand    { get; }

    public ICommand BrowseCommand               { get; }
    public ICommand StartRebaseCommand          { get; }
    public ICommand PauseResumeCommand          { get; }
    public ICommand CancelCommand               { get; }
    public ICommand CancelCalculationCommand    { get; }
    public ICommand OpenFolderCommand           { get; }
    public ICommand ExportLogCommand            { get; }
    /// <summary>Enregistre les jeux de la liste, les paramètres et les règles par jeu dans le fichier de sélection courant.</summary>
    public ICommand SavePresetCommand        { get; }
    public ICommand SavePresetAsCommand      { get; }

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
    /// Initialise le ViewModel, charge les architectures, pré-remplit RebaseItems
    /// et lance l'analyse des fichiers en arrière-plan.
    /// </summary>
    /// <param name="selectedGames">Jeux cochés.</param>
    /// <param name="allGames">Toute la bibliothèque — sert à départager les homonymes de façon stable.</param>
    /// <param name="romStationPath">Dossier d'installation de RomStation.</param>
    /// <param name="dbCopyPath">Copie locale de la base Derby.</param>
    /// <param name="preferences">Préférences utilisateur, ou null pour les valeurs par défaut.</param>
    public RebaseViewModel(
        List<GameItemViewModel> selectedGames,
        List<GameItemViewModel> allGames,
        string romStationPath,
        string dbCopyPath,
        UserPreferences? preferences = null,
        IReadOnlyList<string>? systemNames = null,
        RebasePresetSessionViewModel? session = null)
    {
        _session        = session;
        if (_session != null)
        {
            _session.PropertyChanged += OnSessionChanged;
            PropertyChanged          += OnOwnSettingChanged;
        }
        _selectedGames  = selectedGames;
        _allGames       = allGames;
        _romStationPath = romStationPath;
        _dbCopyPath     = dbCopyPath;
        SystemNames     = systemNames ?? [];

        // Langue des métadonnées : celle de l'interface par défaut
        _metadataLanguageIndex = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr" ? 0 : 1;

        // Initialisation depuis les préférences utilisateur (ou valeurs par défaut si null)
        _preferences = preferences;
        if (preferences != null)
        {
            _targetPath           = preferences.LastRebaseTargetPath;
            _duplicatePolicyIndex = preferences.DuplicatePolicy == "Overwrite" ? 1 : 0;
            _maxParallelCopies    = Math.Clamp(preferences.MaxParallelCopies, 1, 16);
            _retryCount           = Math.Clamp(preferences.RetryCount, 0, 5);
            _retryDelay           = Math.Clamp(preferences.RetryDelaySeconds, 1, 30);
            if (preferences.LastRebaseMetadataLanguage is "fr" or "en")
            {
                _metadataLanguageIndex   = preferences.LastRebaseMetadataLanguage == "fr" ? 0 : 1;
                _metadataLanguageTouched = true;
            }
            // Les options de sortie sont restaurées après LoadArchitectures() ci-dessous
        }

        // Une sélection ouverte depuis un fichier, ou déjà passée par cette fenêtre, l'emporte sur les préférences
        var saved = session?.Settings;
        if (saved != null)
        {
            if (!string.IsNullOrWhiteSpace(saved.TargetPath))
                _targetPath = saved.TargetPath;
            _duplicatePolicyIndex = saved.DuplicatePolicy == "Overwrite" ? 1 : 0;
            _maxParallelCopies    = Math.Clamp(saved.MaxParallelCopies, 1, 16);
            _retryCount           = Math.Clamp(saved.RetryCount, 0, 5);
            _retryDelay           = Math.Clamp(saved.RetryDelaySeconds, 1, 30);
            if (saved.MetadataLanguage is "fr" or "en")
            {
                _metadataLanguageIndex   = saved.MetadataLanguage == "fr" ? 0 : 1;
                _metadataLanguageTouched = true;
            }
        }

        SavePresetCommand   = new RelayCommand(() => OnSavePreset(saveAs: false), () => _session != null && !_isRunning && RebaseItems.Count > 0);
        SavePresetAsCommand = new RelayCommand(() => OnSavePreset(saveAs: true),  () => _session != null && !_isRunning && RebaseItems.Count > 0);

        RemoveItemCommand           = new RelayCommand(param => { if (param is RebaseGameItemViewModel item) RemoveItem(item); }, _ => !_isRunning);
        EditArchitecturesCommand    = new RelayCommand(OnEditArchitectures, () => !_isRunning);
        EditToolsCommand            = new RelayCommand(OnEditTools,         () => !_isRunning);
        LoadTools();
        if (preferences != null) _convertFiles = preferences.LastRebaseConvert;
        if (session?.Settings is { } presetSettings) _convertFiles = presetSettings.Convert;
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

        _suspendReplan = true;
        LoadArchitectures();

        // Restaurer la dernière architecture et ses options de sortie, sinon garder les défauts de l'architecture
        if (preferences != null && !string.IsNullOrWhiteSpace(preferences.LastRebaseArchitectureId))
        {
            var match = Architectures.FirstOrDefault(a => a.Id == preferences.LastRebaseArchitectureId);
            if (match != null)
            {
                SelectedArchitecture = match;
                // Réappliquer les options depuis les préférences — le setter de SelectedArchitecture
                // les a écrasées avec les défauts de l'architecture
                _archiveModeIndex   = preferences.LastRebaseArchiveMode switch { "Copy" => 1, "ExtractAll" => 2, _ => 0 };
                _extractLayoutIndex = preferences.LastRebaseExtractLayout == "Subfolder" ? 1 : 0;
                _copyCovers         = preferences.LastRebaseCopyCovers;
                _generateGamelist   = preferences.LastRebaseGenerateGamelist && match.SupportsGamelist;
            }
            _backupGamelist = preferences.LastRebaseBackupGamelist;
        }

        if (saved != null)
        {
            // Architecture disparue : celle par défaut reste sélectionnée avec ses propres réglages de sortie.
            // L'utilisateur en a été prévenu à l'ouverture du fichier.
            var match = Architectures.FirstOrDefault(a => a.Id == saved.ArchitectureId);
            if (match != null)
            {
                SelectedArchitecture = match;
                _archiveModeIndex   = saved.ArchiveMode switch { "Copy" => 1, "ExtractAll" => 2, _ => 0 };
                _extractLayoutIndex = saved.ExtractLayout == "Subfolder" ? 1 : 0;
                _copyCovers         = saved.CopyCovers;
                _generateGamelist   = saved.GenerateGamelist && match.SupportsGamelist;
            }
            _backupGamelist = saved.BackupGamelist;
        }
        _suspendReplan = false;

        PopulateRebaseItems();
        StatusText = string.Format(Strings.Rebase_Ready, selectedGames.Count);

        // Analyse automatique dès l'ouverture — plan et taille disponibles avant le clic sur Démarrer
        _ = AnalyzeAsync();
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

    /// <summary>
    /// Pose les options de sortie par défaut d'une architecture (jaquettes, gamelist) et remet
    /// le traitement des archives sur « selon l'architecture » : les règles par système font foi.
    /// </summary>
    private void ApplyArchitectureDefaults(ArchitectureEntry arch)
    {
        _archiveModeIndex = 0;
        _copyCovers       = arch.CoversByDefault;
        _generateGamelist = arch.GamelistByDefault && arch.SupportsGamelist;
        OnPropertyChanged(nameof(ArchiveModeIndex));
        OnPropertyChanged(nameof(IsExtractionEnabled));
        OnPropertyChanged(nameof(CopyCovers));
        OnPropertyChanged(nameof(GenerateGamelist));
    }

    /// <summary>Pré-remplit RebaseItems depuis _selectedGames avec Status = Pending.</summary>
    private void PopulateRebaseItems()
    {
        RebaseItems.Clear();
        foreach (var g in _selectedGames)
        {
            var row = new RebaseGameItemViewModel
            {
                GameId          = g.Id,
                Title           = g.Title,
                SystemName      = g.SystemName,
                SystemImagePath = g.SystemImagePath,
                CoverPath       = g.CoverPath,
                CoverExists     = g.CoverExists,
                FileCount       = g.FileCount,
                // Une règle basculée sur la ligne replanifie aussitôt, et compte comme une modification de la présélection
                RuleChanged     = () => { RebuildPlan(); if (_session?.HasFile == true) CaptureSession(); },
            };

            // Règles par jeu retrouvées dans la sélection de travail (fichier ouvert, ou passage précédent dans cette fenêtre)
            if (_session != null && _session.Overrides.TryGetValue(g.Rid, out var o))
                row.RestoreOverrides(o.KeepFileName, o.M3U, o.Extract, o.Transform);

            RebaseItems.Add(row);
        }
    }

    // ── Sélection de travail ──────────────────────────────────────────────

    /// <summary>Paramètres de la fenêtre dans leur état courant, au format du fichier de sélection.</summary>
    private RebasePresetSettings CurrentPresetSettings() => new()
    {
        TargetPath        = _targetPath ?? string.Empty,
        ArchitectureId    = _selectedArchitecture?.Id ?? string.Empty,
        ArchitectureLabel = _selectedArchitecture?.Label ?? string.Empty,
        ArchiveMode       = ArchiveMode.ToString(),
        ExtractLayout     = ExtractLayout.ToString(),
        CopyCovers        = _copyCovers,
        GenerateGamelist  = _generateGamelist,
        BackupGamelist    = _backupGamelist,
        Convert           = _convertFiles,
        MetadataLanguage  = _metadataLanguageTouched ? MetadataLocale : "auto",
        DuplicatePolicy   = _duplicatePolicyIndex == 1 ? "Overwrite" : "Ignore",
        MaxParallelCopies = _maxParallelCopies,
        RetryCount        = _retryCount,
        RetryDelaySeconds = _retryDelay,
    };

    /// <summary>Rend à la sélection de travail les paramètres et les règles par jeu de cette fenêtre.</summary>
    internal void CaptureSession()
    {
        if (_session is null) return;

        var overrides = new Dictionary<int, GameRuleOverrides>();
        foreach (var row in RebaseItems)
        {
            var game = _selectedGames.FirstOrDefault(g => g.Id == row.GameId);
            if (game is null) continue;
            var o = new GameRuleOverrides(row.KeepFileNameOverride, row.M3UOverride, row.ExtractOverride, row.TransformOverride);
            if (!o.IsEmpty) overrides[game.Rid] = o;
        }
        _session.Capture(CurrentPresetSettings(), overrides);
    }

    private void OnSavePreset(bool saveAs)
    {
        if (_session is null) return;
        CaptureSession();
        if (_session.SaveInteractive(OwnerWindow, saveAs))
            StatusText = string.Format(Strings.Preset_Saved, _session.FileName);
        RefreshPresetTexts();
    }

    /// <summary>Suffixe de la barre de titre : « — tests.rsr • ». Vide sans présélection.</summary>
    public string PresetTitleSuffix => _session?.HasFile == true ? "  —  " + _session.DisplayText : string.Empty;

    /// <summary>Rappel au-dessus des options : ce qui se règle ici appartient à la présélection chargée. Null sans présélection (ligne masquée).</summary>
    public string? PresetBanner => _session?.HasFile == true
        ? string.Format(Strings.Rebase_PresetBanner, _session.FileName)
        : null;

    /// <summary>Paramètres qui appartiennent à la présélection : les toucher allume aussitôt son « • », sans attendre la fermeture.</summary>
    private static readonly HashSet<string> PresetSettingNames =
    [
        nameof(TargetPath), nameof(SelectedArchitecture), nameof(ArchiveModeIndex), nameof(ExtractLayoutIndex),
        nameof(CopyCovers), nameof(GenerateGamelist), nameof(BackupGamelist), nameof(MetadataLanguageIndex), nameof(ConvertFiles),
        nameof(DuplicatePolicyIndex), nameof(MaxParallelCopies), nameof(RetryCount), nameof(RetryDelay),
    ];

    private void OnSessionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        => RefreshPresetTexts();

    private void OnOwnSettingChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_suspendReplan || _session?.HasFile != true) return;
        if (e.PropertyName is { } name && PresetSettingNames.Contains(name))
            CaptureSession();
    }

    /// <summary>A la fermeture de la fenêtre : la session vit plus longtemps qu'elle et ne doit pas la retenir.</summary>
    internal void DetachSession()
    {
        if (_session is null) return;
        _session.PropertyChanged -= OnSessionChanged;
        PropertyChanged          -= OnOwnSettingChanged;
    }

    /// <summary>Infobulle du bouton d'enregistrement : dit ce qui est enregistré, et où.</summary>
    public string SavePresetTooltip => _session?.HasFile == true
        ? string.Format(Strings.Rebase_SavePreset_Tooltip_File, _session.FileName)
        : Strings.Rebase_SavePreset_Tooltip_New;

    private void RefreshPresetTexts()
    {
        OnPropertyChanged(nameof(SavePresetTooltip));
        OnPropertyChanged(nameof(PresetTitleSuffix));
        OnPropertyChanged(nameof(PresetBanner));
    }

    // ── Analyse et plan ───────────────────────────────────────────────────

    /// <summary>Mapping de l'architecture sélectionnée, chargé une fois par fichier. Null en cas d'erreur (signalée dans StatusText).</summary>
    private FolderTreeMapping? CurrentMapping()
    {
        if (_selectedArchitecture is null) return null;
        string file = _selectedArchitecture.FolderTreeMapping;
        if (_mappings.TryGetValue(file, out var cached)) return cached;
        try
        {
            var mapping = _archService.LoadFolderTreeMapping(file);
            _mappings[file] = mapping;
            return mapping;
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            return null;
        }
    }

    private static PlanGameInput ToInput(GameItemViewModel g)
        => new(g.Id, g.Title, g.SystemName, g.SystemImagePath, g.Rid, g.CoverPath, g.CoverExists);

    /// <summary>Entrée d'un jeu coché, avec les surcharges de règles posées sur sa ligne.</summary>
    private static PlanGameInput ToInput(GameItemViewModel g, RebaseGameItemViewModel? row)
        => new(g.Id, g.Title, g.SystemName, g.SystemImagePath, g.Rid, g.CoverPath, g.CoverExists,
               row?.KeepFileNameOverride, row?.M3UOverride, row?.ExtractOverride, row?.TransformOverride);

    /// <summary>
    /// Analyse en arrière-plan : fichiers Derby de toute la bibliothèque, inspection des archives,
    /// tailles et arborescences des jeux cochés. Puis construit le plan et la taille estimée.
    /// Retourne -1 si l'analyse est annulée.
    /// </summary>
    private async Task<long> AnalyzeAsync()
    {
        _sizeCts          = new CancellationTokenSource();
        var ct            = _sizeCts.Token;
        IsSizeCalculating = true;
        IsSizeCalculated  = false;
        EstimatedSizeText = string.Empty;
        StatusText        = Strings.Rebase_Analyzing;

        try
        {
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                _filesByGame = _derby.GetGameFiles(_dbCopyPath);

                foreach (var game in _selectedGames)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!_filesByGame.TryGetValue(game.Id, out var files)) continue;

                    foreach (var f in files)
                    {
                        string source = Path.Combine(_romStationPath, "app", f.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                        _fileSizes[source] = SafeFileSize(source);

                        if (ArchiveInspector.IsExtractable(source))
                            _inspector.Inspect(source);
                        else
                        {
                            // Jeu en dossier potentiel (DOS, Windows) : relever l'arborescence une fois
                            string dir = Path.GetDirectoryName(source) ?? string.Empty;
                            if (dir.Length > 0 && !_dirFiles.ContainsKey(dir))
                                _dirFiles[dir] = ListDirectory(dir);
                        }
                    }

                    if (game.CoverExists && game.CoverPath is not null)
                        _fileSizes[game.CoverPath] = SafeFileSize(game.CoverPath);
                }
            }, ct);

            _analysisDone = true;
            RebuildPlan();
            return _estimatedSizeBytes;
        }
        catch (OperationCanceledException)
        {
            _estimatedSizeBytes = -1;
            EstimatedSizeText   = Strings.Rebase_CalculationCancelled;
            IsSizeCalculated    = false;
            StatusText          = string.Format(Strings.Rebase_Ready, _selectedGames.Count);
            return -1;
        }
        catch (Exception ex)
        {
            _estimatedSizeBytes = -1;
            IsSizeCalculated    = false;
            StatusText          = ErrorMessageClassifier.Classify(ex);
            return -1;
        }
        finally
        {
            IsSizeCalculating = false;
        }
    }

    /// <summary>
    /// Recalcule le plan à partir des caches de l'analyse et des options courantes — instantané, sans accès disque.
    /// Sans effet tant que l'analyse n'est pas terminée.
    /// </summary>
    private void RebuildPlan()
    {
        if (_suspendReplan || !_analysisDone || _isRunning) return;
        var mapping = CurrentMapping();
        if (mapping is null || _selectedArchitecture is null)
        {
            // Sans architecture, pas de plan : le statut le dit et Démarrer reste inactif (CanStart)
            if (HasNoArchitecture)
            {
                _plan = null;
                IsSizeCalculated  = false;
                EstimatedSizeText = string.Empty;
                StatusText        = Strings.Rebase_NoArchitecture_Title;
                foreach (var item in RebaseItems) item.Plan = null;
            }
            return;
        }

        var rows = RebaseItems.ToDictionary(i => i.GameId);

        _plan = _planner.Plan(new RebasePlanRequest
        {
            SelectedGames        = _selectedGames.Select(g => ToInput(g, rows.GetValueOrDefault(g.Id))).ToList(),
            AllGames             = _allGames.Select(ToInput).ToList(),
            FilesByGame          = _filesByGame,
            ArchiveLookup        = _inspector.TryGet,
            FileSizeLookup       = p => _fileSizes.TryGetValue(p, out var s) ? s : SafeFileSize(p),
            DirectoryFilesLookup = d => _dirFiles.TryGetValue(d, out var l) ? l : [],
            RomStationPath       = _romStationPath,
            Mapping              = mapping,
            Architecture         = _selectedArchitecture,
            GenerateM3U          = true, // le M3U ne dépend que du marqueur "m3u" du système dans l'architecture
            ArchiveMode          = ArchiveMode,
            Layout               = ExtractLayout,
            CopyCovers           = _copyCovers,
            GenerateGamelist     = _generateGamelist && _selectedArchitecture.SupportsGamelist,
            Convert              = _convertFiles,
            ToolLookup           = LookupTool,
        });

        var byId = _plan.Games.ToDictionary(g => g.GameId);
        foreach (var item in RebaseItems)
        {
            item.Plan = byId.TryGetValue(item.GameId, out var p) ? p : null;

            // Les colonnes Romset / M3U / Extraction redisent l'architecture pour le système de la ligne
            var rule = mapping.FolderTreeMappings.FirstOrDefault(m =>
                string.Equals(m.RomStationSystem, item.SystemName, StringComparison.OrdinalIgnoreCase));
            item.SetArchitectureRules(rule is not null, rule?.KeepFileName ?? false, rule?.M3U ?? false, rule?.Extract ?? false, ArchiveMode);

            // Colonne Conversion : pré-remplie par l'outil que l'architecture désigne pour ce système, et ouverte
            // aux autres outils disponibles qui savent lire ce que contient le jeu
            item.SetConversion(rule?.Transform ?? string.Empty, ToolOptionsFor(item.Plan), _convertFiles);
        }
        OnPropertyChanged(nameof(MissingToolsHint));

        _estimatedSizeBytes = _plan.TotalBytes;
        EstimatedSizeText   = FormatSize(_plan.TotalBytes);
        IsSizeCalculated    = true;
        int unmapped        = _plan.Games.Count(g => g.IsUnmapped);
        StatusText          = string.Format(Strings.Rebase_PlanReady,
            _plan.Games.Count - unmapped,
            _plan.Games.Sum(g => g.Files.Count + g.Covers.Count + g.Playlists.Count))
            + (unmapped > 0 ? string.Format(Strings.Rebase_PlanUnmapped, unmapped) : string.Empty);
    }

    /// <summary>Outils utilisables par ce rebase : ceux dont l'utilisateur a indiqué l'exécutable.</summary>
    private Dictionary<string, (ExternalTool Tool, string ExecutablePath)> AvailableTools()
    {
        var map = new Dictionary<string, (ExternalTool, string)>(StringComparer.OrdinalIgnoreCase);
        foreach (var tool in _tools)
            if (_toolExecutables.TryGetValue(tool.Id, out string? exe))
                map[tool.Id] = (tool, exe);
        return map;
    }

    private static long SafeFileSize(string path)
    {
        try   { return new FileInfo(path).Length; }
        catch { return 0; }
    }

    private static IReadOnlyList<(string RelativePath, long Size)> ListDirectory(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Select(f => (Path.GetRelativePath(dir, f).Replace('\\', '/'), SafeFileSize(f)))
                .ToList();
        }
        catch
        {
            return [];
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
                string userMessage = ErrorMessageClassifier.Classify(ex);
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

        // Plan : utiliser celui déjà calculé, relancer l'analyse si elle avait été annulée
        if (!_analysisDone || _plan is null)
        {
            long analyzed = await AnalyzeAsync();
            if (analyzed < 0 || _plan is null) return; // analyse annulée ou en erreur
        }
        var plan = _plan;

        // Avertissement systèmes sans mapping
        var unmapped = plan.Games.Where(g => g.IsUnmapped)
            .Select(g => g.SystemName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s).ToList();
        if (unmapped.Count > 0)
        {
            string msg = string.Format(Strings.Rebase_Validation_UnmappedSystems,
                unmapped.Count, string.Join(", ", unmapped));
            var dlg = ShowConfirm(Strings.Rebase_Title, msg, Strings.General_Yes, Strings.General_Cancel);
            if (!dlg.Result) return;
        }

        if (!_archService.CheckDiskSpace(_targetPath, plan.TotalBytes))
        {
            double gb = plan.TotalBytes / 1_073_741_824.0;
            ShowConfirm(Strings.Rebase_Title,
                string.Format(Strings.Rebase_Validation_NoSpace, gb.ToString("F1")), "OK");
            return;
        }

        // Conversions : elles passent par un dossier de travail sur le disque local, il lui faut la place du plus gros jeu
        if (plan.MaxWorkBytes > 0 && !_archService.CheckDiskSpace(ExternalToolService.WorkDirectory, plan.MaxWorkBytes))
        {
            double gb = plan.MaxWorkBytes / 1_073_741_824.0;
            ShowConfirm(Strings.Rebase_Title,
                string.Format(Strings.Rebase_Validation_NoWorkSpace, gb.ToString("F1"), ExternalToolService.WorkDirectory), "OK");
            return;
        }

        // Métadonnées : chargées en lot avant le démarrage, dans la langue choisie
        bool withGamelist = _generateGamelist && _selectedArchitecture.SupportsGamelist;
        IReadOnlyDictionary<int, GameMetadata>? metadata = null;
        if (withGamelist)
        {
            try
            {
                string locale = MetadataLocale;
                metadata = await Task.Run(() => _derby.GetGamelistMetadata(_dbCopyPath, locale));
            }
            catch (Exception ex)
            {
                ShowConfirm(Strings.Rebase_Error_Title,
                    string.Format(Strings.Rebase_Error_Message, ErrorMessageClassifier.Classify(ex)), "OK");
                return;
            }
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
            Plan              = plan,
            TargetPath        = _targetPath,
            Architecture      = _selectedArchitecture,
            DuplicatePolicy   = DuplicatePolicy,
            MaxParallelCopies = _maxParallelCopies,
            RetryCount        = _retryCount,
            RetryDelaySeconds = _retryDelay,
            GenerateGamelist  = withGamelist,
            BackupGamelist    = _backupGamelist,
            Metadata          = metadata,
            Tools             = AvailableTools(),
            WorkDirectory     = ExternalToolService.WorkDirectory,
            PauseEvent        = _pauseEvent,
        };

        int metadataFolders = 0;
        var metadataNotes   = new List<string>();
        var progress = new Progress<RebaseProgress>(OnProgressChanged(itemMap, (folders, notes) =>
        {
            metadataFolders = folders;
            metadataNotes   = notes.ToList();
        }));

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

            // En cas d'erreur fatale, les items encore en cours au moment de l'exception
            // restent figés dans cet état s'ils ne sont pas corrigés ici. On les bascule en Failed
            // avec le message d'erreur classifié pour la colonne Erreur du DataGrid.
            if (fatalError != null)
            {
                string itemErrorMessage = ErrorMessageClassifier.Classify(fatalError);
                foreach (var item in RebaseItems.Where(i => i.Status is RebaseItemStatus.Copying or RebaseItemStatus.Extracting or RebaseItemStatus.Converting))
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

            string extra = string.Empty;
            if (withGamelist)
                extra += Environment.NewLine + Environment.NewLine
                       + string.Format(Strings.Rebase_Completed_Metadata, metadataFolders);
            if (metadataNotes.Count > 0)
                extra += Environment.NewLine + string.Join(Environment.NewLine, metadataNotes);

            if (failed == 0 && skipped == 0)
            {
                ShowConfirm(
                    Strings.Rebase_Completed_Title,
                    string.Format(Strings.Rebase_Completed_Success, completed) + extra,
                    "OK");
            }
            else
            {
                ShowConfirm(
                    Strings.Rebase_Completed_Partial_Title,
                    string.Format(Strings.Rebase_Completed_Partial, completed, failed, skipped) + extra,
                    "OK");
            }
        }
    }

    /// <summary>Callback IProgress — met à jour les stats globales et l'item courant sur le thread UI.</summary>
    private Action<RebaseProgress> OnProgressChanged(
        Dictionary<int, RebaseGameItemViewModel> itemMap,
        Action<int, IReadOnlyList<string>> onMetadata)
        => p =>
        {
            GlobalProgress = p.TotalBytes > 0
                ? Math.Min(100, (double)p.CopiedBytes / p.TotalBytes * 100)
                : 0;

            SpeedText  = FormatSpeed(p.SpeedBytesPerSecond);
            EtaText    = FormatEta(p.EstimatedTimeRemaining);
            StatusText = string.Format(Strings.Rebase_GamesCount,
                p.CompletedFiles + p.FailedFiles + p.SkippedFiles, p.TotalFiles);

            if (p.Phase == RebasePhase.WritingMetadata)
            {
                StatusText = Strings.Rebase_Status_WritingMetadata;
                SpeedText  = string.Empty;
                EtaText    = string.Empty;
            }

            if (p.Phase == RebasePhase.Completed)
            {
                StatusText     = string.Format(Strings.Rebase_Completed,
                    p.CompletedFiles, p.FailedFiles, p.SkippedFiles);
                GlobalProgress = 100;
                SpeedText      = string.Empty;
                EtaText        = string.Empty;
                onMetadata(p.MetadataFoldersWritten, p.MetadataNotes);
            }

            if (p.CurrentItem is not null && itemMap.TryGetValue(p.CurrentItem.GameId, out var vm))
            {
                vm.Status      = p.CurrentItem.Status;
                vm.Progress    = p.CurrentItem.Progress;
                vm.ErrorDetail = p.CurrentItem.ErrorDetail;
            }
        };

    // ── Commandes ─────────────────────────────────────────────────────────

    /// <summary>
    /// Retire un jeu de la liste et le décoche dans la bibliothèque, pour que les deux fenêtres
    /// restent d'accord. Le plan et la taille estimée sont recalculés.
    /// </summary>
    private void RemoveItem(RebaseGameItemViewModel item)
    {
        if (_isRunning) return;

        RebaseItems.Remove(item);
        var game = _selectedGames.FirstOrDefault(g => g.Id == item.GameId);
        if (game is not null)
        {
            _selectedGames.Remove(game);
            game.IsSelected = false;
        }

        if (RebaseItems.Count == 0)
            StatusText = string.Format(Strings.Rebase_Ready, 0);
        RebuildPlan();
        OnPropertyChanged(nameof(CanStart));
    }

    /// <summary>Ouvre l'éditeur d'architectures via le callback de la View, puis recharge la liste en gardant la sélection.</summary>
    private void OnEditArchitectures()
    {
        if (OpenArchitectureEditor is null) return;
        OpenArchitectureEditor();
        if (_preferences is not null) _configService.AdoptChildWindowBounds(_preferences);
        LoadTools();               // l'éditeur donne accès à la fenêtre des outils
        ReloadArchitectures();
    }

    /// <summary>Recharge les architectures (fichiers distribués + surcharges utilisateur) et réapplique la sélection par identifiant.</summary>
    internal void ReloadArchitectures()
    {
        string? currentId = _selectedArchitecture?.Id;
        _mappings.Clear();
        Architectures.Clear();

        // Les options choisies par l'utilisateur survivent au rechargement : le setter de
        // SelectedArchitecture réapplique les défauts, on les remet ensuite
        var (archive, covers, gamelist) = (_archiveModeIndex, _copyCovers, _generateGamelist);

        _suspendReplan = true;
        LoadArchitectures();
        var match = currentId is null ? null : Architectures.FirstOrDefault(a => a.Id == currentId);
        if (match is not null)
        {
            SelectedArchitecture = match;
            _archiveModeIndex = archive;
            _copyCovers       = covers;
            _generateGamelist = gamelist && match.SupportsGamelist;
            OnPropertyChanged(nameof(ArchiveModeIndex));
            OnPropertyChanged(nameof(IsExtractionEnabled));
            OnPropertyChanged(nameof(CopyCovers));
            OnPropertyChanged(nameof(GenerateGamelist));
            OnPropertyChanged(nameof(ShowMetadataLanguage));
        }
        _suspendReplan = false;

        OnPropertyChanged(nameof(HasNoArchitecture));
        OnPropertyChanged(nameof(CanStart));
        RebuildPlan();
    }

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

    /// <summary>Annule l'analyse sans confirmation — appelé depuis OnClosing qui gère son propre dialog.</summary>
    internal void StopSizeCalculation() => _sizeCts.Cancel();

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
            w.WriteLine("Titre;Système;Fichiers;Sortie;Statut;Erreur");
            foreach (var item in RebaseItems)
                w.WriteLine($"{item.Title};{item.SystemName};{item.FileCount};{item.OutputText};{item.StatusText};{item.ErrorDetail?.Replace(";", ",")}");
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
        // Même moment que les préférences : la sélection de travail retient ce qui a été réglé ici
        CaptureSession();

        if (_preferences == null) return;

        // Présélection chargée : ses paramètres lui appartiennent et n'écrasent pas les « derniers paramètres » utilisés sans elle.
        // Seule la géométrie de la fenêtre est alors mémorisée.
        if (_session?.HasFile == true)
        {
            if (bounds == null) return;
            try
            {
                _preferences.RebaseWindowBounds = bounds;
                _configService.SaveUserPreferences(_preferences);
            }
            catch { /* confort seulement */ }
            return;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(_targetPath))
                _preferences.LastRebaseTargetPath = _targetPath;
            _preferences.LastRebaseArchitectureId   = _selectedArchitecture?.Id ?? string.Empty;
            _preferences.LastRebaseArchiveMode      = ArchiveMode.ToString();
            _preferences.LastRebaseExtractLayout    = ExtractLayout.ToString();
            _preferences.LastRebaseCopyCovers       = _copyCovers;
            _preferences.LastRebaseGenerateGamelist = _generateGamelist;
            _preferences.LastRebaseBackupGamelist   = _backupGamelist;
            _preferences.LastRebaseConvert          = _convertFiles;
            _preferences.LastRebaseMetadataLanguage = _metadataLanguageTouched ? MetadataLocale : "auto";
            _preferences.DuplicatePolicy            = _duplicatePolicyIndex == 1 ? "Overwrite" : "Ignore";
            _preferences.MaxParallelCopies          = _maxParallelCopies;
            _preferences.RetryCount                 = _retryCount;
            _preferences.RetryDelaySeconds          = _retryDelay;

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
