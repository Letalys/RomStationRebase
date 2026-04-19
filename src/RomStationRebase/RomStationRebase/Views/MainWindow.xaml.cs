using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RomStationRebase.ViewModels;

namespace RomStationRebase.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
    }

    /// <summary>Restaure les bounds mémorisés avant affichage — dernier moment pour éviter un flash visuel.</summary>
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var config   = new Services.ConfigService();
        var prefs    = SafeLoadPrefs(config);
        var defaults = config.LoadWindowDefaults();
        Helpers.WindowStatePersistence.Restore(this, prefs.MainWindowBounds, defaults.MainWindow);

        // Au premier lancement (aucun bounds mémorisés), MainWindow s'ouvre maximisée par défaut.
        // On respecte le choix de l'utilisateur s'il a démaximisé lors de la dernière session.
        if (prefs.MainWindowBounds is null)
            WindowState = WindowState.Maximized;
    }

    /// <summary>Capture et sauvegarde les bounds avant fermeture.</summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            var config = new Services.ConfigService();
            var prefs  = SafeLoadPrefs(config);
            prefs.MainWindowBounds = Helpers.WindowStatePersistence.Capture(this);
            config.SaveUserPreferences(prefs);
        }
        catch
        {
            // Ne pas bloquer la fermeture si la sauvegarde échoue
        }
        base.OnClosing(e);
    }

    /// <summary>Charge UserPreferences ; retourne l'objet par défaut si corruption (évite de bloquer la capture).</summary>
    private static Models.UserPreferences SafeLoadPrefs(Services.ConfigService config)
    {
        try { return config.LoadUserPreferences(); }
        catch { return new Models.UserPreferences(); }
    }

    /// <summary>
    /// Tri par clic sur un en-tête de colonne GridView.
    /// Le Tag du GridViewColumnHeader porte le nom de la colonne ("Title", "System", "Files").
    /// e.OriginalSource garantit le bon ciblage même avec le routing WPF.
    /// </summary>
    private void ListViewColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is GridViewColumnHeader header &&
            header.Tag is string column &&
            DataContext is MainViewModel vm)
        {
            vm.SetSort(column);
            e.Handled = true;
        }
    }

    private void Titlebar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else
            DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => Close();
}
