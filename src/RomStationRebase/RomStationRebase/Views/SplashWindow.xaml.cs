using System.Windows;
using System.Windows.Input;
using RomStationRebase.ViewModels;

namespace RomStationRebase.Views;

/// <summary>Fenêtre de démarrage — affiche la progression de la séquence d'initialisation.</summary>
public partial class SplashWindow : Window
{
    /// <summary>ViewModel de la splash — accessible depuis App.xaml.cs pour récupérer les données chargées.</summary>
    public SplashViewModel ViewModel { get; } = new();

    public SplashWindow()
    {
        InitializeComponent();
        DataContext = ViewModel;

        // Défile vers le bas à chaque avancement — garantit que l'étape en cours est toujours visible.
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SplashViewModel.SplashProgress))
                StepsScrollViewer.ScrollToEnd();
        };
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => DragMove();
}
