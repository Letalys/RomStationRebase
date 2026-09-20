using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace RomStationRebase.Helpers;

/// <summary>
/// Une fenêtre WindowStyle="None" maximisée recouvre la barre des tâches : Windows ne connaît pas
/// son chrome et la traite comme un plein écran. Ce helper répond à WM_GETMINMAXINFO en bornant
/// la taille et la position maximales à la zone de travail de l'écran qui porte la fenêtre.
/// Le cadre de redimensionnement invisible (WS_THICKFRAME, conservé par ResizeMode=CanResize) est
/// repoussé hors de l'écran comme le fait Windows pour toute fenêtre maximisée, sinon il laisse une
/// marge visible et un liseré en haut. A appeler dans OnSourceInitialized, quand le handle existe.
/// </summary>
internal static class WorkAreaMaximizeHelper
{
    public static void Attach(Window window)
    {
        if (PresentationSource.FromVisual(window) is HwndSource source)
            source.AddHook(WndProc);
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != NativeMethods.WM_GETMINMAXINFO) return IntPtr.Zero;

        IntPtr monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero) return IntPtr.Zero;

        var info = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info)) return IntPtr.Zero;

        // Épaisseur du cadre à l'échelle DPI de l'écran courant
        uint dpi    = NativeMethods.GetDpiForWindow(hwnd);
        int  padded = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SM_CXPADDEDBORDER, dpi);
        int  frameX = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SM_CXSIZEFRAME, dpi) + padded;
        int  frameY = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SM_CYSIZEFRAME, dpi) + padded;

        var mmi  = Marshal.PtrToStructure<NativeMethods.MINMAXINFO>(lParam);
        var work = info.rcWork;
        var full = info.rcMonitor;

        // Position et taille exprimées par rapport au coin de l'écran, cadre hors zone visible
        mmi.ptMaxPosition.X = work.Left - full.Left - frameX;
        mmi.ptMaxPosition.Y = work.Top  - full.Top  - frameY;
        mmi.ptMaxSize.X     = work.Right  - work.Left + 2 * frameX;
        mmi.ptMaxSize.Y     = work.Bottom - work.Top  + 2 * frameY;

        Marshal.StructureToPtr(mmi, lParam, fDeleteOld: true);
        handled = true;
        return IntPtr.Zero;
    }
}
