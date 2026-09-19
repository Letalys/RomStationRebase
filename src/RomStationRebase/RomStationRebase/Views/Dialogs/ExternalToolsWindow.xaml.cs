using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using RomStationRebase.ViewModels;

namespace RomStationRebase.Views.Dialogs;

/// <summary>
/// Code-behind de la fenêtre des outils externes — drag de la titlebar, fermeture,
/// persistance des bounds. Toute la logique est dans ExternalToolsViewModel.
/// </summary>
public partial class ExternalToolsWindow : Window
{
    public ExternalToolsWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;

        var vm = new ExternalToolsViewModel();
        DataContext    = vm;
        vm.OwnerWindow = this;
        vm.CloseWindow = Close; // OnClosing demande confirmation si des brouillons sont modifiés
    }

    /// <summary>True si la fenêtre a écrit au moins une fois sur disque.</summary>
    public bool Saved => (DataContext as ExternalToolsViewModel)?.Saved ?? false;

    /// <summary>Restaure les bounds mémorisés avant affichage.</summary>
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var config   = new Services.ConfigService();
        var prefs    = SafeLoadPrefs(config);
        var defaults = config.LoadWindowDefaults();
        Helpers.WindowStatePersistence.Restore(this, prefs.ExternalToolsWindowBounds, defaults.ExternalToolsWindow);

        // Fenêtre sans chrome : bornée à la zone de travail pour ne pas recouvrir la barre des tâches
        Helpers.WorkAreaMaximizeHelper.Attach(this);
    }

    /// <summary>Charge UserPreferences ; retourne l'objet par défaut si corruption (évite de bloquer la capture).</summary>
    private static Models.UserPreferences SafeLoadPrefs(Services.ConfigService config)
    {
        try { return config.LoadUserPreferences(); }
        catch { return new Models.UserPreferences(); }
    }

    /// <summary>Confirme l'abandon des brouillons modifiés, puis mémorise les bounds.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (DataContext is ExternalToolsViewModel vm && !vm.ConfirmClose())
        {
            e.Cancel = true;
            return;
        }

        try
        {
            var config = new Services.ConfigService();
            var prefs  = SafeLoadPrefs(config);
            prefs.ExternalToolsWindowBounds = Helpers.WindowStatePersistence.Capture(this);
            config.SaveUserPreferences(prefs);
        }
        catch
        {
            // La mémorisation des bounds ne doit jamais empêcher la fermeture
        }
        base.OnClosing(e);
    }

    private void Titlebar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => Close();
}
