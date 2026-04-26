using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using RomStationRebase.Resources;
using RomStationRebase.ViewModels;

namespace RomStationRebase.Views.Dialogs;

/// <summary>Code-behind de RebaseWindow — toute la logique métier est dans RebaseViewModel.</summary>
public partial class RebaseWindow : Window
{
    // Drapeau anti-récursion : quand true, OnClosing laisse la fermeture se produire sans dialogue.
    private bool _forceClose;

    /// <summary>
    /// Reçoit le ViewModel en paramètre pour injecter immédiatement OwnerWindow, ConfirmCancel
    /// et ProgressChanged — évite la race condition entre Loaded et l'assignation du DataContext.
    /// </summary>
    public RebaseWindow(RebaseViewModel vm)
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        DataContext    = vm;
        vm.OwnerWindow = this;

        // Confirmation avant annulation du rebase — dialog défini côté View
        vm.ConfirmCancel = () =>
        {
            var dialog = new ConfirmDialog(
                Strings.Rebase_CloseTitle,
                Strings.Rebase_CloseMessage,
                Strings.Rebase_CloseConfirm,
                Strings.General_Cancel) { Owner = this };
            dialog.ShowDialog();
            return dialog.Result;
        };

        // Animation de la barre de progression via l'event ViewModel
        vm.ProgressChanged += AnimateProgressBar;
    }

    /// <summary>Restaure les bounds mémorisés avant affichage.</summary>
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var config   = new Services.ConfigService();
        var prefs    = SafeLoadPrefs(config);
        var defaults = config.LoadWindowDefaults();
        Helpers.WindowStatePersistence.Restore(this, prefs.RebaseWindowBounds, defaults.RebaseWindow);
    }

    /// <summary>Charge UserPreferences ; retourne l'objet par défaut si corruption (évite de bloquer la capture).</summary>
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

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => Close();

    /// <summary>Anime la ProgressBar vers la valeur cible en 300ms — appelée via ProgressChanged.</summary>
    private void AnimateProgressBar(double toValue)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => AnimateProgressBar(toValue));
            return;
        }

        System.Diagnostics.Debug.WriteLine($"[Rebase] AnimateProgressBar: {toValue:F1}%");
        RebaseProgressBar.BeginAnimation(
            ProgressBar.ValueProperty,
            new DoubleAnimation
            {
                To             = toValue,
                Duration       = TimeSpan.FromMilliseconds(300),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
    }

    /// <summary>
    /// Intercepte la fermeture pendant un calcul ou un rebase actif.
    /// Propose d'annuler via un ConfirmDialog avant de laisser la fenêtre se fermer.
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        // Capture les bounds avant toute logique d'annulation — même si la fermeture est annulée,
        // la position sera sauvegardée la prochaine fois que OnClosing passera sans annulation.
        // On ne capture que si la fenêtre n'est pas en train d'être annulée (_forceClose est géré
        // par la branche en dessous qui appelle Close() lui-même).
        if (!_forceClose)
        {
            // Persistance des paramètres de rebase avant la sauvegarde des bounds — l'ordre est
            // important : le rechargement des prefs ci-dessous récupère le chemin à jour sur le disque.
            (DataContext as RebaseViewModel)?.SaveRebasePreferences();

            try
            {
                var config = new Services.ConfigService();
                var prefs  = SafeLoadPrefs(config);
                prefs.RebaseWindowBounds = Helpers.WindowStatePersistence.Capture(this);
                config.SaveUserPreferences(prefs);
            }
            catch
            {
                // Ne pas bloquer la fermeture si la sauvegarde échoue
            }
        }

        if (!_forceClose && DataContext is RebaseViewModel vm && (vm.IsSizeCalculating || vm.IsRunning))
        {
            e.Cancel = true;

            string title   = vm.IsSizeCalculating ? Strings.Rebase_CancelCalcTitle  : Strings.Rebase_CloseTitle;
            string message = vm.IsSizeCalculating ? Strings.Rebase_CancelCalcMessage : Strings.Rebase_CloseMessage;

            var dialog = new ConfirmDialog(title, message, Strings.Rebase_CloseConfirm, Strings.General_Cancel)
            {
                Owner = this,
            };
            dialog.ShowDialog();
            if (dialog.Result)
            {
                _forceClose = true;
                if (vm.IsSizeCalculating)
                    vm.StopSizeCalculation(); // dialog déjà affiché ici, on ne passe pas par la commande
                else
                    vm.StopRebase();          // idem : dialog déjà confirmé, pas de second dialog
                Close();
            }
        }
        base.OnClosing(e);
    }
}
