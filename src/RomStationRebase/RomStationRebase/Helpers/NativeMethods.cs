using System.Runtime.InteropServices;

namespace RomStationRebase.Helpers;

/// <summary>
/// P/Invoke vers user32.dll pour la gestion de fenêtres natives Windows.
/// Utilisé pour le scénario single instance (restaurer et mettre au premier plan une fenêtre
/// existante) et pour borner une fenêtre sans chrome maximisée à la zone de travail de l'écran.
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

    // ── Zone de travail à la maximisation ─────────────────────────────────

    /// <summary>Message envoyé par Windows avant de maximiser : permet de fixer taille et position maximales.</summary>
    internal const int WM_GETMINMAXINFO = 0x0024;

    /// <summary>Écran le plus proche de la fenêtre si elle n'en recouvre aucun.</summary>
    internal const int MONITOR_DEFAULTTONEAREST = 2;

    /// <summary>Largeur du cadre de redimensionnement et bordure de remplissage — à combiner pour l'épaisseur réelle.</summary>
    internal const int SM_CXSIZEFRAME    = 32;
    internal const int SM_CYSIZEFRAME    = 33;
    internal const int SM_CXPADDEDBORDER = 92;

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern int GetSystemMetricsForDpi(int nIndex, uint dpi);

    [DllImport("user32.dll")]
    internal static extern IntPtr MonitorFromWindow(IntPtr hWnd, int dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MONITORINFO
    {
        public int  cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int  dwFlags;
    }
}
