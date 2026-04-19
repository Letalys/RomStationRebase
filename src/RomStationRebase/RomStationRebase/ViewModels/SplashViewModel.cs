using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using RomStationRebase.Helpers;
using RomStationRebase.Models;   // AppState, UserPreferences
using RomStationRebase.Resources;
using RomStationRebase.Services;
using RomStationRebase.Views.Dialogs;

namespace RomStationRebase.ViewModels;

/// <summary>
/// ViewModel de la fenêtre de démarrage.
/// Pilote la séquence d'initialisation : config, détection RS, copie DB, chargement bibliothèque.
/// </summary>
public class SplashViewModel : ViewModelBase
{
    private readonly ConfigService       _config   = new();
    private readonly RomStationService   _rs       = new();
    private readonly DerbyService        _derby    = new();

    private double  _splashProgress;
    private string  _splashStatusText = string.Empty;
    private bool    _hasError;

    // ── Collections et propriétés bindées ────────────────────────────────

    /// <summary>Liste des étapes de démarrage affichées dans la SplashWindow.</summary>
    public ObservableCollection<SplashStep> Steps { get; } = new();

    /// <summary>Avancement global de la séquence (0–100), bindé sur la ProgressBar.</summary>
    public double SplashProgress
    {
        get => _splashProgress;
        set => SetProperty(ref _splashProgress, value);
    }

    /// <summary>Texte de statut affiché dans le footer de la SplashWindow.</summary>
    public string SplashStatusText
    {
        get => _splashStatusText;
        set => SetProperty(ref _splashStatusText, value);
    }

    /// <summary>True si une étape a échoué — rend le bouton Quit visible.</summary>
    public bool HasError
    {
        get => _hasError;
        set => SetProperty(ref _hasError, value);
    }

    /// <summary>Version de l'application lue depuis l'assembly — affichée dans la SplashWindow.</summary>
    public string AppVersion { get; } =
        "v" + (Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.1.0");

    // ── Données chargées (accédées par App.xaml.cs après succès) ─────────

    /// <summary>Chemin de la copie locale de la base Derby — disponible après RunStartupSequence.</summary>
    public string DbCopyPath { get; private set; } = string.Empty;

    /// <summary>Chemin de l'installation RomStation — disponible après RunStartupSequence.</summary>
    public string RomStationPath { get; private set; } = string.Empty;

    /// <summary>État de l'application mis à jour et sauvegardé pendant la séquence.</summary>
    public AppState LoadedAppState { get; private set; } = new();

    /// <summary>Préférences utilisateur chargées pendant la séquence.</summary>
    public UserPreferences LoadedPreferences { get; private set; } = new();

    // ── Commandes ─────────────────────────────────────────────────────────

    /// <summary>Ferme proprement l'application depuis la SplashWindow (cas d'erreur).</summary>
    public ICommand QuitCommand { get; } =
        new RelayCommand(() => Application.Current.Shutdown());

    // ── Initialisation ────────────────────────────────────────────────────

    public SplashViewModel()
    {
        Steps.Add(new SplashStep(Strings.Splash_LoadingConfig));
        Steps.Add(new SplashStep(Strings.Splash_DetectingRS));
        Steps.Add(new SplashStep(Strings.Splash_ReadingConfig));
        Steps.Add(new SplashStep(Strings.Splash_CheckingDB));
        Steps.Add(new SplashStep(Strings.Splash_CopyingDB));
        Steps.Add(new SplashStep(Strings.Splash_VerifyingDB));
        Steps.Add(new SplashStep(Strings.Splash_Starting));
    }

    // ── Séquence de démarrage ─────────────────────────────────────────────

    /// <summary>
    /// Exécute les 7 étapes de démarrage dans l'ordre.
    /// Les mises à jour UI se font sur le thread appelant (thread UI).
    /// Les opérations I/O lourdes sont enveloppées dans Task.Run.
    /// Retourne true si toutes les étapes critiques ont réussi.
    /// </summary>
    public async Task<bool> RunStartupSequence()
    {
        const int total = 7;

        void SetRunning(int i)
        {
            Steps[i].Status = SplashStepStatus.Running;
            SplashProgress  = (double)i / total * 100;
        }
        void SetSuccess(int i, string? detail = null)
        {
            Steps[i].Status = SplashStepStatus.Success;
            if (detail != null) Steps[i].Detail = detail;
            SplashProgress = (double)(i + 1) / total * 100;
        }
        void SetWarning(int i, string detail)
        {
            Steps[i].Status = SplashStepStatus.Warning;
            Steps[i].Detail = detail;
            SplashProgress  = (double)(i + 1) / total * 100;
        }
        void Fail(int i, string detail)
        {
            Steps[i].Status = SplashStepStatus.Error;
            Steps[i].Detail = detail;
            HasError        = true;
        }

        // ── Étape 0 : Chargement configuration ───────────────────────────
        SetRunning(0);
        SplashStatusText = Strings.Splash_LoadingConfig + "…";

        AppState appState;
        UserPreferences prefs;

        try
        {
            appState = await Task.Run(() => _config.LoadAppState());
        }
        catch (ConfigCorruptedException ex)
        {
            var dlg = new ConfirmDialog(
                "Corrupted configuration",
                string.Format(Strings.Splash_ConfigCorrupted_State, ex.Detail),
                "Continue", "Quit");
            dlg.ShowDialog();
            if (!dlg.Result) { Fail(0, ex.Detail); return false; }
            _config.DeleteAppState();
            appState = new AppState();
        }

        try
        {
            prefs = await Task.Run(() => _config.LoadUserPreferences());
        }
        catch (ConfigCorruptedException ex)
        {
            var dlg = new ConfirmDialog(
                "Corrupted preferences",
                string.Format(Strings.Splash_ConfigCorrupted_Prefs, ex.Detail),
                "Reset", "Quit");
            dlg.ShowDialog();
            if (!dlg.Result) { Fail(0, ex.Detail); return false; }
            _config.DeleteUserPreferences();
            prefs = new UserPreferences();
        }

        LoadedPreferences = prefs;
        SetSuccess(0);

        // ── Étape 1 : Détection RomStation ───────────────────────────────
        SetRunning(1);
        SplashStatusText = Strings.Splash_DetectingRS + "…";

        string? rsPath = appState.RomStationPath;
        bool pathValid = rsPath != null
            && await Task.Run(() => _rs.ValidateRomStationPath(rsPath));

        if (!pathValid)
        {
            // 1. Tentative via le registre Windows
            rsPath = await Task.Run(() => _rs.DetectRomStationPath());

            // 2. Si échec registre : proposer la sélection manuelle (installation ZIP portable)
            if (rsPath is null)
            {
                rsPath = await PromptForManualRomStationPathAsync();

                if (rsPath is null)
                {
                    // L'utilisateur a cliqué "Quitter" ou annulé — étape marquée en échec
                    Fail(1, Strings.Splash_RSNotFound_StepFailed);
                    return false;
                }

                // Sauvegarder le chemin choisi manuellement pour les prochains lancements
                appState.RomStationPath = rsPath;
            }
        }

        appState.RomStationPath = rsPath;
        RomStationPath = rsPath!;
        SetSuccess(1, string.Format(Strings.Splash_RSFound, rsPath));

        // ── Étape 2 : Lecture de RomStation.cfg ──────────────────────────
        SetRunning(2);
        SplashStatusText = Strings.Splash_ReadingConfig + "…";

        try
        {
            var (version, derbyVer) = await Task.Run(
                () => _rs.ParseRomStationConfig(rsPath!));
            appState.RomStationVersion = version;
            appState.DerbyVersion      = derbyVer;
            await Task.Run(() => _config.SaveAppState(appState));
            SetSuccess(2, $"v{version} — Derby {derbyVer}");
        }
        catch (Exception ex)
        {
            Fail(2, ex.Message);
            return false;
        }

        // ── Étape 3 : Vérification du verrou ─────────────────────────────
        SetRunning(3);
        SplashStatusText = Strings.Splash_CheckingDB + "…";

        bool locked = await Task.Run(() => _rs.IsDatabaseLocked(rsPath!));
        if (locked)
            SetWarning(3, Strings.Splash_RSOpen);
        else
            SetSuccess(3);

        // ── Étape 4 : Copie de la base ────────────────────────────────────
        SetRunning(4);
        SplashStatusText = Strings.Splash_CopyingDB + "…";

        string dbCopyPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RomStationRebase", "database");

        var copyProgress = new Progress<string>(msg => SplashStatusText = msg);
        bool copyOk = await _rs.CopyDatabaseAsync(rsPath!, dbCopyPath, copyProgress);

        if (!copyOk) { Fail(4, Strings.Splash_DBLocked); return false; }

        appState.DatabaseCopyPath = dbCopyPath;
        DbCopyPath = dbCopyPath;
        SetSuccess(4);

        // ── Étape 5 : Vérification intégrité ─────────────────────────────
        SetRunning(5);
        SplashStatusText = Strings.Splash_VerifyingDB + "…";

        bool connOk = await _rs.TestDatabaseConnectionAsync(dbCopyPath);
        if (!connOk)
        {
            Fail(5, Strings.Splash_DBCorrupted);
            return false;
        }

        // Enregistre la date de sync — le chargement réel de la bibliothèque se fait dans MainWindow
        appState.LastSyncDate = DateTime.Now;
        await Task.Run(() => _config.SaveAppState(appState));

        LoadedAppState = appState;
        SetSuccess(5);

        // ── Étape 6 : Démarrage (cosmétique) ─────────────────────────────
        // Donne le temps à l'utilisateur de lire les étapes et à la barre d'atteindre 95%.
        SetRunning(6);
        SplashStatusText = Strings.Splash_Starting;
        SplashProgress   = 95;
        await Task.Delay(600);

        // Laisse voir la coche verte 200ms avant que le splash ne se ferme
        SetSuccess(6);
        SplashProgress = 100;
        await Task.Delay(200);

        return true;
    }

    /// <summary>
    /// Affiche un popup invitant l'utilisateur à sélectionner manuellement le dossier RomStation.
    /// Cas typique : installation via ZIP portable, pas d'entrée dans le registre Windows.
    /// Retourne le chemin validé, ou null si l'utilisateur choisit de quitter.
    /// Boucle jusqu'à ce qu'un dossier valide soit sélectionné ou que l'utilisateur quitte.
    /// </summary>
    private async Task<string?> PromptForManualRomStationPathAsync()
    {
        while (true)
        {
            // Afficher le popup principal : "Parcourir..." ou "Quitter"
            bool browseRequested = await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var dlg = new Views.Dialogs.ConfirmDialog(
                    Strings.Splash_RSNotFound_Title,
                    Strings.Splash_RSNotFound_Message,
                    primaryLabel:   Strings.Splash_RSNotFound_Browse,
                    secondaryLabel: Strings.Splash_RSNotFound_Quit)
                {
                    Owner = Application.Current.MainWindow,
                };
                dlg.ShowDialog();
                return dlg.Result;
            });

            // L'utilisateur a cliqué "Quitter"
            if (!browseRequested)
                return null;

            // L'utilisateur a cliqué "Parcourir" — afficher le sélecteur de dossier natif WPF
            string? selectedPath = await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var folderDialog = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = Strings.Splash_RSNotFound_SelectFolder,
                };
                bool? r = folderDialog.ShowDialog();
                return r == true ? folderDialog.FolderName : null;
            });

            // L'utilisateur a annulé le sélecteur — retour au popup initial
            if (string.IsNullOrEmpty(selectedPath))
                continue;

            // Valider que le dossier contient bien app/database et app/RomStation.cfg
            bool valid = await Task.Run(() => _rs.ValidateRomStationPath(selectedPath));

            if (valid)
                return selectedPath;

            // Dossier invalide : popup d'erreur, puis retour au popup initial
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var errorDlg = new Views.Dialogs.ConfirmDialog(
                    Strings.Splash_RSNotFound_Title,
                    Strings.Splash_RSNotFound_InvalidFolder,
                    primaryLabel: "OK")
                {
                    Owner = Application.Current.MainWindow,
                };
                errorDlg.ShowDialog();
            });
        }
    }
}
