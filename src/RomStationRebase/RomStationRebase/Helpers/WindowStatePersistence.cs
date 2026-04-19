using System.Windows;
using RomStationRebase.Models;

namespace RomStationRebase.Helpers;

/// <summary>
/// Helper centralisé pour restaurer et capturer l'état (position, taille, maximisé) des fenêtres.
/// Gère le multi-écran : si la position mémorisée est hors de tout écran connecté, recentre la fenêtre.
/// </summary>
public static class WindowStatePersistence
{
    /// <summary>
    /// Applique les dimensions à une fenêtre AVANT son affichage.
    /// À appeler depuis OnSourceInitialized (pas Loaded — trop tard pour éviter un flash).
    /// </summary>
    /// <param name="win">Fenêtre à configurer</param>
    /// <param name="saved">Bounds mémorisés de l'utilisateur, ou null au premier lancement</param>
    /// <param name="defaultSize">Taille par défaut si saved est null ou invalide</param>
    public static void Restore(Window win, WindowBounds? saved, WindowSize defaultSize)
    {
        // Cas 1 : rien de mémorisé ou position hors écran → taille par défaut + centrage manuel
        // sur l'écran principal. On évite WindowStartupLocation=CenterScreen qui n'a
        // pas d'effet dans OnSourceInitialized (la fenêtre est déjà positionnée Win32).
        if (saved is null || !IsOnScreen(saved.Left, saved.Top, saved.Width, saved.Height))
        {
            win.WindowStartupLocation = WindowStartupLocation.Manual;
            win.Width  = defaultSize.Width;
            win.Height = defaultSize.Height;
            win.Left   = (SystemParameters.PrimaryScreenWidth  - defaultSize.Width)  / 2;
            win.Top    = (SystemParameters.PrimaryScreenHeight - defaultSize.Height) / 2;
            return;
        }

        // Cas 2 : on restaure les bounds mémorisés
        win.WindowStartupLocation = WindowStartupLocation.Manual;
        win.Left   = saved.Left;
        win.Top    = saved.Top;
        win.Width  = saved.Width;
        win.Height = saved.Height;

        // L'état maximisé est appliqué après le positionnement — important pour que
        // la démaximisation restaure les Left/Top/Width/Height ci-dessus
        if (saved.IsMaximized)
            win.WindowState = WindowState.Maximized;
    }

    /// <summary>
    /// Capture les bounds de la fenêtre pour sauvegarde.
    /// Gère correctement le cas maximisé en utilisant RestoreBounds au lieu de Width/Height.
    /// </summary>
    public static WindowBounds Capture(Window win)
    {
        // Si la fenêtre est maximisée, RestoreBounds contient la taille "en dessous"
        // qu'on veut retrouver après démaximisation
        if (win.WindowState == WindowState.Maximized)
        {
            var rb = win.RestoreBounds;
            return new WindowBounds
            {
                Left        = rb.Left,
                Top         = rb.Top,
                Width       = rb.Width,
                Height      = rb.Height,
                IsMaximized = true,
            };
        }

        return new WindowBounds
        {
            Left        = win.Left,
            Top         = win.Top,
            Width       = win.Width,
            Height      = win.Height,
            IsMaximized = false,
        };
    }

    /// <summary>
    /// Vérifie que le rectangle donné intersecte la zone virtuelle couvrant l'ensemble des écrans.
    /// Utilise SystemParameters WPF (VirtualScreen*) — aucune dépendance WinForms.
    /// Couvre le cas multi-écran : si la position mémorisée était sur un écran secondaire
    /// désormais déconnecté, le rectangle est hors zone et la fenêtre sera recentrée.
    /// </summary>
    private static bool IsOnScreen(double left, double top, double width, double height)
    {
        double vLeft   = SystemParameters.VirtualScreenLeft;
        double vTop    = SystemParameters.VirtualScreenTop;
        double vRight  = vLeft + SystemParameters.VirtualScreenWidth;
        double vBottom = vTop  + SystemParameters.VirtualScreenHeight;

        // Il suffit que le rectangle intersecte la zone virtuelle (pas nécessairement entièrement dedans)
        return left < vRight  && (left + width)  > vLeft
            && top  < vBottom && (top  + height) > vTop;
    }
}
