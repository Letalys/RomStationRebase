using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using RomStationRebase.Models;
using RomStationRebase.Services;
using RomStationRebase.ViewModels;
using RomStationRebase.Views;
using RomStationRebase.Views.Dialogs;

namespace RomStationRebase;

/// <summary>Point d'entrée WPF — initialise la culture, gère les exceptions globales et pilote SplashWindow → MainWindow.</summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ── Handlers globaux — enregistrés en premier pour capturer tout crash précoce ──
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException       += OnUnobservedTaskException;
        DispatcherUnhandledException                += OnDispatcherUnhandledException;

        // Charge les préférences silencieusement pour appliquer la langue avant l'affichage
        UserPreferences? prefs = null;
        try { prefs = new ConfigService().LoadUserPreferences(); }
        catch { /* sera traité par SplashViewModel */ }

        ApplyCulture(prefs);
        RunStartupAsync();
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
        string msg = FormatException(e.Exception);
        Debug.WriteLine($"[App] DispatcherUnhandledException:\n{msg}");

        ShowErrorDialog("Unhandled Error", msg);
        Shutdown(1);
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

        Dispatcher.Invoke(() => ShowErrorDialog("Unhandled Task Error", msg));
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
            Dispatcher.Invoke(() => ShowErrorDialog("Fatal Error", msg));
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
    /// Tout le corps est protégé par un try/catch global pour capturer
    /// les exceptions inattendues et les afficher avant fermeture.
    /// </summary>
    private async void RunStartupAsync()
    {
        try
        {
            var splash = new SplashWindow();
            MainWindow = splash;
            splash.Show();

            bool success = await splash.ViewModel.RunStartupSequence();

            if (!success)
            {
                // La SplashWindow affiche déjà l'étape en erreur (icône rouge + détail).
                // On ferme proprement sans jamais instancier MainWindow.
                Application.Current.Shutdown();
                return;
            }

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
            await vm.LoadLibraryAsync(dbCopyPath, romStationPath);
        }
        catch (Exception ex)
        {
            string msg = FormatException(ex);
            Debug.WriteLine($"[App] RunStartupAsync fatal:\n{msg}");

            await Dispatcher.InvokeAsync(() =>
            {
                ShowErrorDialog("Startup Error", msg);
                Shutdown(1);
            });
        }
    }
}
