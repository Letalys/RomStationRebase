using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RomStationRebase.ViewModels;
using WpfToolkit.Controls;

namespace RomStationRebase.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        // Abonnement au Loaded pour synchroniser la taille des vignettes après rendu initial
        Loaded += OnLoaded;
    }

    /// <summary>Restaure les bounds mémorisés avant affichage — dernier moment pour éviter un flash visuel.</summary>
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var config   = new Services.ConfigService();
        var prefs    = SafeLoadPrefs(config);
        var defaults = config.LoadWindowDefaults();
        Helpers.WindowStatePersistence.Restore(this, prefs.MainWindowBounds, defaults.MainWindow);

        // Au premier lancement (aucun bounds mémorisés), MainWindow s'ouvre maximisée par défaut.
        if (prefs.MainWindowBounds is null)
            WindowState = WindowState.Maximized;
    }

    /// <summary>
    /// Applique la taille initiale des vignettes et s'abonne aux changements du ViewModel.
    /// Séparé de OnSourceInitialized : l'arbre visuel complet est disponible ici.
    /// </summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.PropertyChanged += OnMainViewModelPropertyChanged;
            ApplyThumbnailSize(vm.ThumbnailSize);
        }

        // Quand MosaicListView redevient visible (bascule Liste→Mosaïque), le VirtualizingWrapPanel
        // n'existe dans l'arbre visuel que si le ListView est Visible — on réapplique la taille.
        MosaicListView.IsVisibleChanged += (_, args) =>
        {
            if ((bool)args.NewValue && DataContext is MainViewModel vm2)
                ApplyThumbnailSize(vm2.ThumbnailSize);
        };
    }

    /// <summary>
    /// Répercute les changements de ThumbnailSize sur les éléments non-bindables en XAML.
    /// Exception MVVM justifiée :
    ///   - VirtualizingWrapPanel.ItemSize est de type Size (non DependencyProperty) ;
    ///   - GridViewColumn.Width n'est pas une DependencyProperty.
    /// Aucun autre raccourci propre n'est disponible pour ces deux propriétés.
    /// </summary>
    private void OnMainViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ThumbnailSize) && sender is MainViewModel vm)
            ApplyThumbnailSize(vm.ThumbnailSize);
    }

    /// <summary>Met à jour ItemSize du VirtualizingWrapPanel et la largeur de la colonne miniature.</summary>
    private void ApplyThumbnailSize(string size)
    {
        bool grand = size == "Grand";

        // VirtualizingWrapPanel.ItemSize — type Size, non-DP : binding XAML impossible
        var panel = FindVisualChild<VirtualizingWrapPanel>(MosaicListView);
        if (panel is not null)
            panel.ItemSize = grand ? new Size(320, 419) : new Size(216, 295);

        // GridViewColumn.Width — non-DP : binding XAML impossible
        CoverColumn.Width = grand ? 108 : 52;
    }

    /// <summary>Traverse l'arbre visuel en profondeur pour trouver le premier enfant du type T.</summary>
    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) return found;
            var result = FindVisualChild<T>(child);
            if (result is not null) return result;
        }
        return null;
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
