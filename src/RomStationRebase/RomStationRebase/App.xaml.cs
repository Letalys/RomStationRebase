using RomStationRebase.Helpers;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using RomStationRebase.Models;
using RomStationRebase.Resources;
using RomStationRebase.Services;
using RomStationRebase.ViewModels;
using RomStationRebase.Views;
using RomStationRebase.Views.Dialogs;

namespace RomStationRebase;

/// <summary>Point d'entrée WPF — initialise la culture, gère les exceptions globales et pilote SplashWindow → MainWindow.</summary>
public partial class App : Application
{
    private System.Threading.Mutex? _singleInstanceMutex;

    // Présélection à ouvrir : reçue en argument (double-clic dans l'Explorateur) ou relayée par une seconde instance.
    // Elle attend que la bibliothèque soit chargée et qu'aucune fenêtre modale ne soit ouverte.
    private SingleInstanceChannel? _channel;
    private MainViewModel?         _mainVm;
    private string?                _pendingPresetPath;

    protected override void OnStartup(StartupEventArgs e)
    {
        // === Instance unique : acquis en tout premier, avant toute autre logique ===
        // Tout retard expose au risque qu'une seconde instance affiche son splash
        // ou commence à charger la base avant d'être stoppée.
        const string mutexName = @"Global\RomStationRebase-SingleInstance";
        bool isNewInstance;
        try
        {
            _singleInstanceMutex = new System.Threading.Mutex(true, mutexName, out isNewInstance);
        }
        catch (System.Threading.AbandonedMutexException)
        {
            // La 1ère instance a crashé sans relâcher proprement le Mutex.
            // Windows nous le donne quand même, on le considère comme acquis.
            isNewInstance = true;
        }

        string? presetArg = ShellOpenService.PresetPathFromArgs(e.Args);

        if (!isNewInstance)
        {
            // Une autre instance tourne déjà : lui confier la présélection à ouvrir (ou simplement lui demander de se montrer),
            // puis se fermer. Si elle ne répond pas, on se rabat sur la mise au premier plan par handle de fenêtre.
            if (!SingleInstanceChannel.TrySend(presetArg))
                BringExistingInstanceToFront();
            Shutdown(0);
            return;
        }

        base.OnStartup(e);

        // A l'écoute dès le splash : une demande reçue trop tôt reste en attente
        _pendingPresetPath = presetArg;
        _channel = new SingleInstanceChannel();
        _channel.Start(OnRemoteRequest);

        // L'utilisateur a associé les .rsrgp à RSR, puis déplacé son dossier : le double-clic doit continuer de marcher
        if (Environment.ProcessPath is { } exe)
            FileAssociationService.RepairIfMoved(exe, Strings.Preset_FileFilter);

        // ── Handlers globaux — enregistrés en premier pour capturer tout crash précoce ──
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException       += OnUnobservedTaskException;
        DispatcherUnhandledException                += OnDispatcherUnhandledException;

        // Charge les préférences silencieusement pour appliquer la langue avant l'affichage
        UserPreferences? prefs = null;
        try { prefs = new ConfigService().LoadUserPreferences(); }
        catch { /* sera traité par SplashViewModel */ }

        ApplyCulture(prefs);
        ApplyTheme(prefs);
        RunStartupAsync();
    }

    /// <summary>
    /// Recherche l'autre instance de RSRebase en cours d'exécution et restaure
    /// sa fenêtre principale (ou son splash s'il est encore à l'écran) au
    /// premier plan. Échec silencieux si introuvable.
    /// </summary>
    private static void BringExistingInstanceToFront()
    {
        try
        {
            var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
            var processes = System.Diagnostics.Process.GetProcessesByName(currentProcess.ProcessName);
            foreach (var process in processes)
            {
                if (process.Id == currentProcess.Id) continue;
                IntPtr handle = process.MainWindowHandle;
                if (handle == IntPtr.Zero) continue;

                if (Helpers.NativeMethods.IsIconic(handle))
                    Helpers.NativeMethods.ShowWindow(handle, Helpers.NativeMethods.SW_RESTORE);
                Helpers.NativeMethods.SetForegroundWindow(handle);
                return;
            }
        }
        catch
        {
            // Échec silencieux : si l'autre instance est introuvable, la nouvelle
            // se ferme quand même. Dégradation gracieuse acceptable pour ce cas rare.
        }
    }

    // ── Ouverture d'une présélection depuis l'Explorateur ────────────────────────

    /// <summary>Demande d'une seconde instance, reçue hors du thread d'interface.</summary>
    private void OnRemoteRequest(string? rawPath) => Dispatcher.BeginInvoke(() =>
    {
        ActivateTopWindow();
        if (ShellOpenService.ValidatePresetPath(rawPath) is { } path)
            _pendingPresetPath = path; // la dernière demande l'emporte
        TryOpenPendingPreset();
    });

    /// <summary>
    /// Ouvre la présélection en attente dès que c'est possible : bibliothèque chargée, aucune fenêtre modale.
    /// Une fenêtre de rebase ouverte (a fortiori un rebase en cours) n'est jamais interrompue : la présélection
    /// s'ouvrira quand elle se refermera.
    /// </summary>
    private void TryOpenPendingPreset()
    {
        if (_pendingPresetPath is null || _mainVm is null || _mainVm.IsLoading) return;
        if (System.Windows.Interop.ComponentDispatcher.IsThreadModal) return;

        string path = _pendingPresetPath;
        _pendingPresetPath = null;
        _mainVm.OpenPresetFile(path);
    }

    /// <summary>Montre la fenêtre la plus haute de l'application : la modale ouverte s'il y en a une, sinon la fenêtre principale.</summary>
    private void ActivateTopWindow()
    {
        if (MainWindow is { } main && main.WindowState == WindowState.Minimized)
            main.WindowState = WindowState.Normal;
        var top = Windows.OfType<Window>().LastOrDefault(w => w.IsVisible) ?? MainWindow;
        top?.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _channel?.Dispose();
        try
        {
            _singleInstanceMutex?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Le Mutex n'était pas détenu par ce thread : ignorer.
        }
        finally
        {
            _singleInstanceMutex?.Dispose();
            _singleInstanceMutex = null;
        }

        base.OnExit(e);
    }

    // ── Handlers d'exceptions globales ───────────────────────────────────────

    /// <summary>
    /// Exception non gérée sur le thread Dispatcher (thread UI).
    /// Marque l'exception comme gérée pour éviter le crash brutal,
    /// affiche le détail dans un ConfirmDialog, puis ferme l'application.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;

        // Une erreur de mise en page se reproduit à chaque passe : le dialogue d'erreur pompe les messages,
        // l'erreur revient, un autre dialogue s'ouvre par-dessus, et la pile finit par déborder. Un seul dialogue ;
        // si l'erreur tourne en boucle au point de figer l'interface, l'application s'arrête d'elle-même.
        if (_fatalErrorCount++ > 0)
        {
            if (_fatalErrorCount > 200) Environment.Exit(1);
            return;
        }

        string msg = FormatException(e.Exception);
        Debug.WriteLine($"[App] DispatcherUnhandledException:\n{msg}");
        WriteCrashReport(ErrorCodes.UnhandledUi + Environment.NewLine + msg);

        ShowErrorDialog("Unhandled Error [" + ErrorCodes.UnhandledUi + "]", msg);
        Shutdown(1);
    }

    private int _fatalErrorCount;

    /// <summary>Garde le détail de l'erreur à côté des journaux de rebase : le dialogue peut ne jamais pouvoir s'afficher.</summary>
    private static void WriteCrashReport(string message)
    {
        try
        {
            string dir = RebaseLogWriter.LogDirectory;
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, $"RSR_crash_{DateTime.Now:yyyyMMdd_HHmmss}.txt"), message);
        }
        catch
        {
            // Rien de plus à faire dans un gestionnaire d'erreur fatale
        }
    }

    /// <summary>
    /// Exception non observée dans une Task (ex. Task.Run sans await ni .Wait()).
    /// SetObserved() empêche le crash du process ; on logue et on affiche le détail.
    /// </summary>
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
        string msg = FormatException(e.Exception);
        Debug.WriteLine($"[App] UnobservedTaskException:\n{msg}");

        Dispatcher.Invoke(() => ShowErrorDialog("Unhandled Task Error [" + ErrorCodes.UnhandledTask + "]", msg));
    }

    /// <summary>
    /// Exception fatale sur n'importe quel thread (IsTerminating peut être true).
    /// Tente d'afficher un ConfirmDialog, fallback sur MessageBox si le Dispatcher est instable.
    /// </summary>
    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex  = e.ExceptionObject as Exception;
        string msg = ex != null ? FormatException(ex) : e.ExceptionObject?.ToString() ?? "Unknown fatal error";
        Debug.WriteLine($"[App] UnhandledException (IsTerminating={e.IsTerminating}):\n{msg}");

        try
        {
            WriteCrashReport(ErrorCodes.UnhandledFatal + Environment.NewLine + msg);
            Dispatcher.Invoke(() => ShowErrorDialog("Fatal Error [" + ErrorCodes.UnhandledFatal + "]", msg));
        }
        catch
        {
            // Dernier recours si le Dispatcher est dans un état instable
            MessageBox.Show(msg, "Fatal Error — RomStation Rebase",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Formate une exception avec son type, son message et sa StackTrace complète.</summary>
    private static string FormatException(Exception ex)
    {
        var inner = ex.InnerException != null ? $"\n\nInner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}" : string.Empty;
        return $"{ex.GetType().FullName}: {ex.Message}{inner}\n\nStackTrace:\n{ex.StackTrace}";
    }

    /// <summary>
    /// Affiche un ConfirmDialog d'erreur. Utilise toujours un seul bouton "OK" (bouton secondaire vide).
    /// </summary>
    private static void ShowErrorDialog(string title, string message)
    {
        try
        {
            // secondaryLabel = null → le bouton secondaire est masqué (voir ConfirmDialog.xaml.cs)
            var dlg = new ConfirmDialog(title, message, "OK");
            dlg.ShowDialog();
        }
        catch
        {
            // ConfirmDialog lui-même a échoué — repli sur la boîte native
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Cycle de vie SplashWindow → MainWindow ────────────────────────────────

    /// <summary>
    /// Applique la culture de l'interface selon UserPreferences.AppLanguage.
    /// "auto" → détection système, "fr" → fr-FR, "en" (ou autre) → en.
    /// </summary>
    private static void ApplyTheme(UserPreferences? prefs)
        => ThemeService.Apply(prefs?.Theme ?? "Light");

    private static void ApplyCulture(UserPreferences? prefs)
    {
        string langCode;

        if (prefs?.AppLanguage == "fr")
            langCode = "fr";
        else if (prefs?.AppLanguage == "en")
            langCode = "en";
        else
            langCode = Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName;

        CultureInfo uiCulture = langCode == "fr"
            ? CultureInfo.GetCultureInfo("fr-FR")
            : CultureInfo.GetCultureInfo("en");

        Thread.CurrentThread.CurrentUICulture = uiCulture;
    }

    /// <summary>
    /// Gère le cycle de vie SplashWindow → MainWindow.
    /// En cas d'erreur non récupérable, affiche un ConfirmDialog spécifique puis ferme l'application.
    /// Les cas de fermeture volontaire (clic Quitter dans un dialog de résolution RS) sont déjà
    /// traités par Shutdown() direct dans SplashViewModel — RunStartupSequence retourne ShuttingDown.
    /// </summary>
    private async void RunStartupAsync()
    {
        var splash = new SplashWindow();
        MainWindow = splash;
        splash.Show();

        StartupResult result;
        try
        {
            result = await splash.ViewModel.RunStartupSequence();
        }
        catch (Exception ex)
        {
            // Exception non prévue ayant échappé à RunStartupSequence
            string detail = ex.Message;
            Debug.WriteLine($"[App] RunStartupAsync fatal:\n{FormatException(ex)}");

            await Dispatcher.InvokeAsync(() =>
            {
                var dlg = new ConfirmDialog(
                    Strings.Splash_UnexpectedError_Title,
                    ErrorCodes.Tag(string.Format(Strings.Splash_UnexpectedError_Message, detail), ErrorCodes.StartupFailed),
                    Strings.Splash_UnexpectedError_Quit)
                { Owner = splash };
                dlg.ShowDialog();
                Shutdown(1);
            });
            return;
        }

        switch (result)
        {
            case StartupResult.ShuttingDown:
                // Shutdown() déjà appelé dans SplashViewModel — ne rien faire
                return;

            case StartupResult.DatabaseCorrupted:
                // Base Derby corrompue détectée au step 5 — l'icône rouge est déjà visible sur le splash
                await Dispatcher.InvokeAsync(() =>
                {
                    var dlg = new ConfirmDialog(
                        Strings.Splash_DBCorrupted_Title,
                        ErrorCodes.Tag(Strings.Splash_DBCorrupted, ErrorCodes.DatabaseIncomplete),
                        Strings.Splash_RSNotFound_Quit)
                    { Owner = splash };
                    dlg.ShowDialog();
                    Shutdown(1);
                });
                return;

            case StartupResult.DatabaseNotInitialized:
                // Base Derby non initialisée détectée au step 3 — RomStation jamais lancé
                await Dispatcher.InvokeAsync(() =>
                {
                    var dlg = new ConfirmDialog(
                        Strings.Splash_DBNotInitialized_Title,
                        ErrorCodes.Tag(Strings.Splash_DBNotInitialized_Message, ErrorCodes.DatabaseNotInitialized),
                        Strings.Splash_DBNotInitialized_Quit)
                    { Owner = splash };
                    dlg.ShowDialog();
                    Shutdown(1);
                });
                return;

            case StartupResult.Failed:
                // Erreur technique (step 2 ou step 4) — le détail est déjà visible sur le splash
                await Dispatcher.InvokeAsync(() =>
                {
                    var dlg = new ConfirmDialog(
                        Strings.Splash_UnexpectedError_Title,
                        ErrorCodes.Tag(string.Format(Strings.Splash_UnexpectedError_Message, string.Empty), ErrorCodes.StartupFailed),
                        Strings.Splash_UnexpectedError_Quit)
                    { Owner = splash };
                    dlg.ShowDialog();
                    Shutdown(1);
                });
                return;
        }

        // ── Success : ouvrir MainWindow ───────────────────────────────────────
        // Seulement ici — séquence complète garantie — on crée MainWindow.
        // ContentRendered se déclenche quand WPF a réellement dessiné le contenu
        // à l'écran ; c'est le bon moment pour fermer le splash sans effet de flash.
        var dbCopyPath     = splash.ViewModel.DbCopyPath;
        var romStationPath = splash.ViewModel.RomStationPath;
        var loadedPrefs    = splash.ViewModel.LoadedPreferences;

        var tcs = new TaskCompletionSource<bool>();
        MainViewModel vm = null!;

        await Dispatcher.InvokeAsync(() =>
        {
            vm = new MainViewModel(loadedPrefs);
            var main = new MainWindow();
            main.DataContext = vm;

            main.ContentRendered += (_, _) => tcs.TrySetResult(true);

            MainWindow = main;
            main.Show();
        });

        // Attendre que MainWindow soit vraiment rendue avant de fermer le splash
        await tcs.Task;
        await Dispatcher.InvokeAsync(() => splash.Close());

        // Chargement lazy de la bibliothèque — s'exécute dans MainWindow avec ProgressBar animée
        try
        {
            await vm.LoadLibraryAsync(dbCopyPath, romStationPath);
        }
        catch (Exception ex)
        {
            string detail = ex.Message;
            Debug.WriteLine($"[App] LoadLibraryAsync fatal:\n{FormatException(ex)}");

            await Dispatcher.InvokeAsync(() =>
            {
                var dlg = new ConfirmDialog(
                    Strings.Splash_UnexpectedError_Title,
                    ErrorCodes.Tag(string.Format(Strings.Splash_UnexpectedError_Message, detail), ErrorCodes.LibraryLoadFailed),
                    Strings.Splash_UnexpectedError_Quit);
                dlg.ShowDialog();
                Shutdown(1);
            });
            return;
        }

        // Bibliothèque prête : la présélection reçue en argument peut s'ouvrir, et celles qui arriveront plus tard aussi.
        // Hors du try ci-dessus : une erreur dans la fenêtre de rebase ne doit pas passer pour un échec de chargement.
        _ = Dispatcher.BeginInvoke(() =>
        {
            _mainVm = vm;
            vm.PropertyChanged += (_, a) =>
            {
                if (a.PropertyName == nameof(MainViewModel.IsLoading)) TryOpenPendingPreset(); // fin d'une synchronisation
            };
            System.Windows.Interop.ComponentDispatcher.LeaveThreadModal += (_, _) =>
                Dispatcher.BeginInvoke(TryOpenPendingPreset, DispatcherPriority.ApplicationIdle);
            TryOpenPendingPreset();
        });
    }
}
