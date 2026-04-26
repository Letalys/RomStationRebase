using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using RomStationRebase.ViewModels;

namespace RomStationRebase.Views.Dialogs;

/// <summary>
/// Code-behind du panneau Paramètres — drag de la titlebar, fermeture, et persistance des bounds.
/// Toute la logique métier est dans SettingsViewModel.
/// </summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        DataContext = vm;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var config   = new Services.ConfigService();
        var prefs    = SafeLoadPrefs(config);
        var defaults = config.LoadWindowDefaults();
        Helpers.WindowStatePersistence.Restore(this, prefs.SettingsWindowBounds, defaults.SettingsWindow);
    }

    private static Models.UserPreferences SafeLoadPrefs(Services.ConfigService config)
    {
        try { return config.LoadUserPreferences(); }
        catch { return new Models.UserPreferences(); }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        try
        {
            var config = new Services.ConfigService();
            var prefs  = SafeLoadPrefs(config);
            prefs.SettingsWindowBounds = Helpers.WindowStatePersistence.Capture(this);
            config.SaveUserPreferences(prefs);
        }
        catch { }
        base.OnClosing(e);
    }

    private void Titlebar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
