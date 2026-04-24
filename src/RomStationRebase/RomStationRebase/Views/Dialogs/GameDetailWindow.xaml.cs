using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using RomStationRebase.ViewModels;

namespace RomStationRebase.Views.Dialogs;

/// <summary>Code-behind de GameDetailWindow — toute la logique métier sera dans GameDetailViewModel.</summary>
public partial class GameDetailWindow : Window
{
    public GameDetailWindow(GameDetailViewModel vm)
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        DataContext = vm;
    }

    /// <summary>Restaure les bounds mémorisés avant affichage.</summary>
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var config   = new Services.ConfigService();
        var prefs    = SafeLoadPrefs(config);
        var defaults = config.LoadWindowDefaults();
        Helpers.WindowStatePersistence.Restore(this, prefs.GameDetailWindowBounds, defaults.GameDetailWindow);
    }

    /// <summary>Charge UserPreferences ; retourne l'objet par défaut si corruption.</summary>
    private static Models.UserPreferences SafeLoadPrefs(Services.ConfigService config)
    {
        try { return config.LoadUserPreferences(); }
        catch { return new Models.UserPreferences(); }
    }

    private void Titlebar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => Close();

    /// <summary>Fermeture via la touche Escape.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
        base.OnKeyDown(e);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        try
        {
            var config = new Services.ConfigService();
            var prefs  = SafeLoadPrefs(config);
            prefs.GameDetailWindowBounds = Helpers.WindowStatePersistence.Capture(this);
            config.SaveUserPreferences(prefs);
        }
        catch
        {
            // Ne pas bloquer la fermeture si la sauvegarde échoue
        }
        base.OnClosing(e);
    }
}
