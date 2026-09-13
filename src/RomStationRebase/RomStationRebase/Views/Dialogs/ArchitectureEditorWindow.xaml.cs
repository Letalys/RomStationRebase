using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using RomStationRebase.ViewModels;

namespace RomStationRebase.Views.Dialogs;

/// <summary>
/// Code-behind de l'éditeur d'architectures cibles — drag de la titlebar, fermeture,
/// persistance des bounds. Toute la logique est dans ArchitectureEditorViewModel.
/// </summary>
public partial class ArchitectureEditorWindow : Window
{
    /// <param name="systemNames">Noms de systèmes proposés dans la colonne Système ; vide = saisie libre seule.</param>
    public ArchitectureEditorWindow(IReadOnlyList<string> systemNames)
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;

        var vm = new ArchitectureEditorViewModel(systemNames);
        DataContext    = vm;
        vm.OwnerWindow = this;
        vm.CloseWindow = Close; // OnClosing demande confirmation si des brouillons sont modifiés

        // Les erreurs de chargement attendent que la fenêtre soit visible pour avoir un Owner valide
        Loaded += (_, _) => vm.ReportPendingErrors();
    }

    /// <summary>True si l'éditeur a écrit au moins une fois sur disque — l'appelant recharge alors ses architectures.</summary>
    public bool Saved => (DataContext as ArchitectureEditorViewModel)?.Saved ?? false;

    /// <summary>Restaure les bounds mémorisés avant affichage.</summary>
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var config   = new Services.ConfigService();
        var prefs    = SafeLoadPrefs(config);
        var defaults = config.LoadWindowDefaults();
        Helpers.WindowStatePersistence.Restore(this, prefs.ArchitectureEditorWindowBounds, defaults.ArchitectureEditorWindow);

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
        if (DataContext is ArchitectureEditorViewModel vm && !vm.ConfirmClose())
        {
            e.Cancel = true;
            return;
        }

        try
        {
            var config = new Services.ConfigService();
            var prefs  = SafeLoadPrefs(config);
            prefs.ArchitectureEditorWindowBounds = Helpers.WindowStatePersistence.Capture(this);
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
