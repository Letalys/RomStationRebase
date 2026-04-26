using System.Runtime.InteropServices;

namespace RomStationRebase.Helpers;

/// <summary>
/// P/Invoke vers user32.dll pour la gestion de fenêtres natives Windows.
/// Utilisé exclusivement pour le scénario single instance (restaurer et
/// mettre au premier plan une fenêtre existante d'une autre instance).
/// </summary>
internal static class NativeMethods
{
    /// <summary>Indique si la fenêtre est actuellement minimisée (iconisée).</summary>
    [DllImport("user32.dll")]
    internal static extern bool IsIconic(IntPtr hWnd);

    /// <summary>Affiche, masque ou modifie l'état d'affichage d'une fenêtre.</summary>
    [DllImport("user32.dll")]
    internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    /// <summary>Donne le focus et place la fenêtre au premier plan.</summary>
    [DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>Restaure une fenêtre minimisée ou maximisée à sa taille et position d'origine.</summary>
    internal const int SW_RESTORE = 9;
}
