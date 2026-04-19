using System.Windows;
using System.Windows.Input;
using RomStationRebase.ViewModels;

namespace RomStationRebase.Views.Dialogs;

/// <summary>
/// Code-behind du panneau Paramètres — uniquement le drag de la titlebar et la fermeture.
/// Toute la logique métier est dans SettingsViewModel.
/// </summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void Titlebar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
