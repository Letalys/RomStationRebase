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
            // Injection du callback de scroll — délègue la mécanique WPF (non-DP) au code-behind.
            // Pattern identique à ConfirmCancel dans RebaseWindow.
            vm.ScrollToLetter = ScrollToLetter;
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

    /// <summary>Remonte l'arbre visuel pour trouver le premier ancêtre du type T.</summary>
    private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
    {
        var parent = VisualTreeHelper.GetParent(child);
        while (parent is not null)
        {
            if (parent is T found) return found;
            parent = VisualTreeHelper.GetParent(parent);
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
    // Saut instantané vers la première occurrence de la lettre.
    // Calcul d'offset direct (ScrollToVerticalOffset) plutôt que
    // ScrollIntoView : (1) positionne l'item en HAUT de la zone visible
    // au lieu du comportement "minimal" de ScrollIntoView ;
    // (2) évite le coût de matérialisation des conteneurs intermédiaires
    // avec VirtualizingWrapPanel en mode mosaïque.
    private void ScrollToLetter(string letter)
    {
        if (DataContext is not MainViewModel vm) return;

        // 1) Identifier l'index du premier jeu correspondant à la lettre
        int targetIndex = -1;
        var games = vm.FilteredGames;
        for (int i = 0; i < games.Count; i++)
        {
            if (string.IsNullOrEmpty(games[i].Title)) continue;

            char premiere = games[i].Title[0];
            string bucket = char.IsLetter(premiere) && char.ToUpper(premiere) is >= 'A' and <= 'Z'
                ? char.ToUpper(premiere).ToString()
                : "#";

            if (bucket == letter) { targetIndex = i; break; }
        }
        if (targetIndex < 0) return;

        // 2) Identifier le ListView actif et son ScrollViewer
        ListView listView = vm.IsMosaicView ? MosaicListView : ListListView;
        var scrollViewer = FindVisualChild<ScrollViewer>(listView);
        if (scrollViewer == null) return;

        // 3) Calcul de l'offset vertical selon le mode d'affichage
        double targetOffset;
        if (vm.IsMosaicView)
        {
            // Wrap panel multi-colonnes : pixel-based, calcul colonnes × lignes.
            // Note : la ligne contenant le premier item cible est placée en haut de la
            // zone visible. Si l'item cible n'est pas en début de ligne (autres lettres
            // à sa gauche), celles-ci restent visibles — comportement standard d'une
            // grille, cohérent avec le layout en colonnes.
            int columnsPerRow = Math.Max(1, (int)(scrollViewer.ViewportWidth / vm.MosaicItemWidth));
            int row = targetIndex / columnsPerRow;
            targetOffset = row * vm.MosaicItemHeight;
        }
        else
        {
            // Mode liste : item-based (VirtualizingStackPanel, ScrollUnit.Item par défaut).
            // ScrollToVerticalOffset interprète son argument comme un index, pas des pixels.
            targetOffset = targetIndex;
        }

        // 4) Plafonner pour ne pas dépasser la fin de liste
        targetOffset = Math.Clamp(targetOffset, 0, scrollViewer.ScrollableHeight);

        // 5) Scroll direct sans matérialisation des conteneurs intermédiaires
        scrollViewer.ScrollToVerticalOffset(targetOffset);
    }

    /// <summary>
    /// Ouvre la fiche détail au double-clic sur une tuile ou une ligne.
    /// Ignoré si le double-clic a eu lieu sur un Button (case à cocher, bouton œil).
    /// </summary>
    private void GameListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        // Ne pas déclencher si le double-clic est sur un Button ou à l'intérieur d'un Button
        if (e.OriginalSource is DependencyObject src &&
            (src is Button || FindVisualParent<Button>(src) is not null))
            return;

        if (e.OriginalSource is FrameworkElement fe && fe.DataContext is GameItemViewModel game)
        {
            vm.OpenGameDetailCommand.Execute(game.Id);
            e.Handled = true;
        }
    }

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
