using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows;
using RomStationRebase.Helpers;
using RomStationRebase.Models;
using RomStationRebase.Resources;
using RomStationRebase.Services;
using RomStationRebase.Views.Dialogs;

namespace RomStationRebase.ViewModels;

/// <summary>Résultat de la séquence de démarrage, transmis à App.xaml.cs pour piloter la suite.</summary>
public enum StartupResult
{
    /// <summary>Toutes les étapes ont réussi — MainWindow peut s'ouvrir.</summary>
    Success,
    /// <summary>La base Derby est corrompue (test de connexion step 5 échoué).</summary>
    DatabaseCorrupted,
    /// <summary>La base Derby n'est pas encore initialisée — RomStation jamais lancé (step 3 échoué).</summary>
    DatabaseNotInitialized,
    /// <summary>Une erreur technique non récupérable s'est produite (step 2 ou step 4).</summary>
    Failed,
    /// <summary>L'utilisateur a cliqué Quitter dans un dialog — Shutdown() a déjà été appelé.</summary>
    ShuttingDown
}

/// <summary>
/// ViewModel de la fenêtre de démarrage.
/// Pilote la séquence d'initialisation : config, détection RS, copie DB, chargement bibliothèque.
/// Le splash est passif : toutes les interactions utilisateur passent par des dialogs ConfirmDialog.
/// </summary>
public class SplashViewModel : ViewModelBase
{
    private readonly ConfigService     _config = new();
    private readonly RomStationService _rs     = new();
    private readonly DerbyService      _derby  = new();

    private double _splashProgress;
    private string _splashStatusText = string.Empty;

    // ── Collections et propriétés bindées ────────────────────────────────────

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

    /// <summary>Version de l'application lue depuis l'assembly — affichée dans la SplashWindow.</summary>
    public string AppVersion { get; } =
        "v" + (Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.1.0");

    // ── Données chargées (accédées par App.xaml.cs après succès) ─────────────

    /// <summary>Chemin de la copie locale de la base Derby — disponible après RunStartupSequence.</summary>
    public string DbCopyPath { get; private set; } = string.Empty;

    /// <summary>Chemin de l'installation RomStation — disponible après RunStartupSequence.</summary>
    public string RomStationPath { get; private set; } = string.Empty;

    /// <summary>État de l'application mis à jour et sauvegardé pendant la séquence.</summary>
    public AppState LoadedAppState { get; private set; } = new();

    /// <summary>Préférences utilisateur chargées pendant la séquence.</summary>
    public UserPreferences LoadedPreferences { get; private set; } = new();

    // ── Initialisation ────────────────────────────────────────────────────────

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

    // ── Séquence de démarrage ─────────────────────────────────────────────────

    /// <summary>
    /// Exécute les 7 étapes de démarrage dans l'ordre.
    /// Retourne Success si tout a réussi, DatabaseCorrupted si le test Derby a échoué,
    /// Failed pour toute autre erreur technique, ShuttingDown si l'utilisateur a quitté.
    /// </summary>
    public async Task<StartupResult> RunStartupSequence()
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
            // Marque l'étape en erreur (icône rouge) — le splash passif n'a plus de bouton Quit
            Steps[i].Status = SplashStepStatus.Error;
            Steps[i].Detail = detail;
        }

        // ── Étape 0 : Chargement configuration ───────────────────────────────
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
            if (!dlg.Result)
            {
                // L'utilisateur refuse la réinitialisation — fermeture immédiate
                Application.Current.Shutdown();
                return StartupResult.ShuttingDown;
            }
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
            if (!dlg.Result)
            {
                Application.Current.Shutdown();
                return StartupResult.ShuttingDown;
            }
            _config.DeleteUserPreferences();
            prefs = new UserPreferences();
        }

        LoadedPreferences = prefs;
        SetSuccess(0);

        // ── Étape 1 : Détection RomStation ───────────────────────────────────
        SetRunning(1);
        SplashStatusText = Strings.Splash_DetectingRS + "…";

        string? rsPath = await ResolveRomStationPathAsync(appState);

        if (rsPath is null)
        {
            // ResolveRomStationPathAsync a appelé Shutdown() suite au clic Quitter de l'utilisateur
            return StartupResult.ShuttingDown;
        }

        RomStationPath = rsPath;
        SetSuccess(1, string.Format(Strings.Splash_RSFound, rsPath));

        // ── Étape 2 : Lecture de RomStation.cfg ──────────────────────────────
        SetRunning(2);
        SplashStatusText = Strings.Splash_ReadingConfig + "…";

        try
        {
            var (version, derbyVer) = await Task.Run(
                () => _rs.ParseRomStationConfig(rsPath));
            appState.RomStationVersion = version;
            appState.DerbyVersion      = derbyVer;
            await Task.Run(() => _config.SaveAppState(appState));
            SetSuccess(2, $"v{version} — Derby {derbyVer}");
        }
        catch (Exception ex)
        {
            Fail(2, ex.Message);
            return StartupResult.Failed;
        }

        // ── Étape 3 : Vérification de la base de données ─────────────────────
        SetRunning(3);
        SplashStatusText = Strings.Splash_CheckingDB + "…";

        // Vérification 3a — La base Derby a-t-elle été initialisée ?
        bool initialized = await Task.Run(() => _rs.IsDatabaseInitialized(rsPath));
        if (!initialized)
        {
            Fail(3, Strings.Splash_DBNotInitialized_StepLabel);
            return StartupResult.DatabaseNotInitialized;
        }

        // Vérification 3b — Les fichiers internes Derby sont-ils complets ?
        bool filesComplete = await Task.Run(() => _rs.IsDatabaseFilesComplete(rsPath));
        if (!filesComplete)
        {
            Fail(3, Strings.Splash_DBCorrupted);
            return StartupResult.DatabaseCorrupted;
        }

        // Vérification 3c — La base est-elle verrouillée (RomStation ouvert en parallèle) ?
        bool locked = await Task.Run(() => _rs.IsDatabaseLocked(rsPath));
        if (locked)
            SetWarning(3, Strings.Splash_RSOpen);
        else
            SetSuccess(3);

        // ── Étape 4 : Copie de la base ────────────────────────────────────────
        SetRunning(4);
        SplashStatusText = Strings.Splash_CopyingDB + "…";

        string dbCopyPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RomStationRebase", "database");

        var copyProgress = new Progress<string>(msg => SplashStatusText = msg);
        bool copyOk = await _rs.CopyDatabaseAsync(rsPath, dbCopyPath, copyProgress);

        if (!copyOk)
        {
            Fail(4, Strings.Splash_DBLocked);
            return StartupResult.Failed;
        }

        appState.DatabaseCopyPath = dbCopyPath;
        DbCopyPath = dbCopyPath;
        SetSuccess(4);

        // ── Étape 5 : Vérification intégrité ─────────────────────────────────
        SetRunning(5);
        SplashStatusText = Strings.Splash_VerifyingDB + "…";

        bool connOk = await _rs.TestDatabaseConnectionAsync(dbCopyPath);
        if (!connOk)
        {
            Fail(5, Strings.Splash_DBCorrupted);
            return StartupResult.DatabaseCorrupted;
        }

        // Enregistre la date de sync — le chargement réel de la bibliothèque se fait dans MainWindow
        appState.LastSyncDate = DateTime.Now;
        await Task.Run(() => _config.SaveAppState(appState));

        LoadedAppState = appState;
        SetSuccess(5);

        // ── Étape 6 : Démarrage (cosmétique) ─────────────────────────────────
        // Donne le temps à l'utilisateur de lire les étapes et à la barre d'atteindre 95%.
        SetRunning(6);
        SplashStatusText = Strings.Splash_Starting;
        SplashProgress   = 95;
        await Task.Delay(600);

        // Laisse voir la coche verte 200ms avant que le splash ne se ferme
        SetSuccess(6);
        SplashProgress = 100;
        await Task.Delay(200);

        return StartupResult.Success;
    }

    // ── Résolution du chemin RomStation ──────────────────────────────────────

    /// <summary>
    /// Résout le chemin d'installation RomStation en trois phases :
    ///   Phase 1 : vérification silencieuse du chemin persisté dans AppState.
    ///   Phase 2 : vérification silencieuse du chemin détecté dans le registre Windows.
    ///   Phase 3 : boucle interactive — dialogs Parcourir/Quitter jusqu'à un chemin valide.
    /// Critère unique : présence de app\RomStation.cfg (marqueur d'une installation RS).
    /// La vérification de la base Derby est déléguée au step 3.
    /// Retourne le chemin validé, ou null si Shutdown() a été appelé (clic Quitter de l'utilisateur).
    /// Met à jour AppState.RomStationPath avec le chemin final.
    /// </summary>
    private async Task<string?> ResolveRomStationPathAsync(AppState appState)
    {
        // ── Phase 1 : chemin persisté dans AppState ───────────────────────────
        if (appState.RomStationPath is not null)
        {
            bool valid = await Task.Run(() => _rs.ValidateRomStationPath(appState.RomStationPath));
            if (valid)
                return appState.RomStationPath;
        }

        // ── Phase 2 : détection via le registre Windows ───────────────────────
        string? registryPath = await Task.Run(() => _rs.DetectRomStationPath());
        if (registryPath is not null)
        {
            bool valid = await Task.Run(() => _rs.ValidateRomStationPath(registryPath));
            if (valid)
            {
                appState.RomStationPath = registryPath;
                return registryPath;
            }
        }

        // ── Phase 3 : boucle interactive ─────────────────────────────────────
        string dialogTitle   = Strings.Splash_RSNotFound_Title;
        string dialogMessage = Strings.Splash_RSNotFound_Message;

        while (true)
        {
            // Afficher le dialog Parcourir/Quitter et attendre le choix de l'utilisateur
            string? selectedPath = await ShowBrowseOrQuitDialogAsync(dialogTitle, dialogMessage);

            if (selectedPath is null)
                return null; // Shutdown() déjà appelé

            bool valid = await Task.Run(() => _rs.ValidateRomStationPath(selectedPath));

            if (valid)
            {
                appState.RomStationPath = selectedPath;
                return selectedPath;
            }

            // Dossier sélectionné ne contient pas RomStation.cfg
            dialogTitle   = Strings.Splash_RSNotFound_Title;
            dialogMessage = Strings.Splash_RSNotFound_InvalidFolder;
        }
    }

    /// <summary>
    /// Affiche un dialog Parcourir/Quitter, puis ouvre le sélecteur de dossier si Parcourir est cliqué.
    /// Retourne le chemin sélectionné (non vide), ou null si Quitter a été cliqué
    /// (dans ce cas Application.Current.Shutdown() a déjà été appelé).
    /// Boucle si l'utilisateur ferme le sélecteur sans choisir de dossier.
    /// </summary>
    private async Task<string?> ShowBrowseOrQuitDialogAsync(string title, string message)
    {
        while (true)
        {
            // Afficher le dialog principal avec les boutons Parcourir et Quitter
            bool browseRequested = await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var dlg = new ConfirmDialog(title, message,
                    primaryLabel:   Strings.Splash_RSNotFound_Browse,
                    secondaryLabel: Strings.Splash_RSNotFound_Quit)
                { Owner = Application.Current.MainWindow };
                dlg.ShowDialog();
                return dlg.Result;
            });

            if (!browseRequested)
            {
                // L'utilisateur a cliqué Quitter — fermeture immédiate sans retour au splash
                Application.Current.Shutdown();
                return null;
            }

            // Ouvrir le sélecteur de dossier natif WPF
            string? selectedPath = await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var folderDialog = new Microsoft.Win32.OpenFolderDialog
                { Title = Strings.Splash_RSNotFound_SelectFolder };
                bool? r = folderDialog.ShowDialog();
                return r == true ? folderDialog.FolderName : null;
            });

            if (!string.IsNullOrEmpty(selectedPath))
                return selectedPath;

            // L'utilisateur a annulé le sélecteur de dossier — re-afficher le même dialog
        }
    }
}
