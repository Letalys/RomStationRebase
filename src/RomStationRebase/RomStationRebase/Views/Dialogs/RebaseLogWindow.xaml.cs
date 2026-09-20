using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using RomStationRebase.Helpers;
using RomStationRebase.ViewModels;

namespace RomStationRebase.Views.Dialogs;

/// <summary>
/// Fenêtre du journal de rebase. Indépendante : sans propriétaire, présente dans la barre des tâches,
/// et utilisable pendant qu'une fenêtre modale (le rebase lui-même) est ouverte. Une seule à la fois :
/// demander un autre journal le charge dans la fenêtre déjà ouverte.
/// </summary>
public partial class RebaseLogWindow : Window
{
    private const int WM_ENABLE = 0x000A;

    private static RebaseLogWindow? _current;

    private RebaseLogWindow(string filePath)
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;

        var vm = new RebaseLogViewModel(filePath);
        DataContext    = vm;
        vm.ScrollToEnd = ScrollToLastLine;
        Loaded += (_, _) => vm.Start();
    }

    /// <summary>Ouvre le journal donné, dans la fenêtre existante si elle est déjà là.</summary>
    public static void ShowFor(string filePath)
    {
        if (_current is null)
        {
            _current = new RebaseLogWindow(filePath);
            _current.Show();
            return;
        }

        (_current.DataContext as RebaseLogViewModel)?.Load(filePath);
        if (_current.WindowState == WindowState.Minimized) _current.WindowState = WindowState.Normal;
        _current.Activate();
    }

    /// <summary>Ferme la fenêtre du journal avec l'application : sans cela elle la maintiendrait en vie.</summary>
    public static void CloseIfOpen() => _current?.Close();

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var config   = new Services.ConfigService();
        var prefs    = SafeLoadPrefs(config);
        var defaults = config.LoadWindowDefaults();
        WindowStatePersistence.Restore(this, prefs.RebaseLogWindowBounds, defaults.RebaseLogWindow);
        WorkAreaMaximizeHelper.Attach(this);

        // ShowDialog désactive toutes les fenêtres du thread. Celle-ci ne fait que lire un fichier :
        // elle se réactive aussitôt, pour qu'on puisse suivre le journal pendant que le rebase tourne.
        if (PresentationSource.FromVisual(this) is HwndSource source)
            source.AddHook(StayEnabledHook);
    }

    private static IntPtr StayEnabledHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_ENABLE && wParam == IntPtr.Zero)
            NativeMethods.EnableWindow(hwnd, true);
        return IntPtr.Zero;
    }

    private static Models.UserPreferences SafeLoadPrefs(Services.ConfigService config)
    {
        try { return config.LoadUserPreferences(); }
        catch { return new Models.UserPreferences(); }
    }

    private void ScrollToLastLine()
    {
        if (LinesGrid.Items.Count > 0)
            LinesGrid.ScrollIntoView(LinesGrid.Items[^1]);
    }

    /// <summary>Remonter à la molette arrête le suivi : sinon chaque nouvelle ligne ramènerait en bas.</summary>
    private void LinesGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Delta > 0 && DataContext is RebaseLogViewModel vm) vm.Follow = false;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        (DataContext as RebaseLogViewModel)?.Stop();
        try
        {
            new Services.ConfigService().SaveRebaseLogWindowBounds(WindowStatePersistence.Capture(this));
        }
        catch
        {
            // La mémorisation des bounds ne doit jamais empêcher la fermeture
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (ReferenceEquals(_current, this)) _current = null;
        base.OnClosed(e);
    }

    private void Titlebar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

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
