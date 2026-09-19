using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace RomStationRebase.Services;

/// <summary>État de l'association des présélections (.rsr) pour l'utilisateur courant.</summary>
public enum PresetAssociationState
{
    /// <summary>Aucune association vers RSR.</summary>
    None,
    /// <summary>Association de l'utilisateur vers cet exécutable.</summary>
    ThisExecutable,
    /// <summary>Association de l'utilisateur vers un autre emplacement de RSR (dossier déplacé, autre copie).</summary>
    OtherExecutable,
}

/// <summary>
/// Associe l'extension .rsr à RomStation Rebase, pour l'utilisateur courant seulement (HKCU\Software\Classes) :
/// aucun droit administrateur, aucune écriture hors de son profil. Geste explicite depuis les Paramètres,
/// jamais silencieux : une application qui peut aussi se distribuer en ZIP n'écrit pas dans le registre sans qu'on le lui demande.
/// </summary>
public static class FileAssociationService
{
    private const string ProgId  = "RomStationRebase.Preset";

    /// <summary>Identifiant des toutes premières associations, du temps où la présélection s'appelait « projet » : nettoyé au passage.</summary>
    private const string LegacyProgId = "RomStationRebase.Project";
    private const string Classes = @"Software\Classes\";

    private static string Extension => RebasePresetService.Extension;

    private static string CommandFor(string exePath) => $"\"{exePath}\" \"%1\"";

    public static PresetAssociationState GetState(string exePath)
    {
        try
        {
            using var ext = Registry.CurrentUser.OpenSubKey(Classes + Extension);
            // Une association posée sous l'ancien identifiant est traitée comme absente : le bouton des Paramètres la remplacera
            if (ext?.GetValue(string.Empty) as string != ProgId) return PresetAssociationState.None;

            using var cmd = Registry.CurrentUser.OpenSubKey(Classes + ProgId + @"\shell\open\command");
            string? command = cmd?.GetValue(string.Empty) as string;
            if (string.IsNullOrEmpty(command)) return PresetAssociationState.None;

            return string.Equals(command, CommandFor(exePath), StringComparison.OrdinalIgnoreCase)
                ? PresetAssociationState.ThisExecutable
                : PresetAssociationState.OtherExecutable;
        }
        catch
        {
            return PresetAssociationState.None;
        }
    }

    public static void Register(string exePath, string friendlyTypeName)
    {
        RemoveLegacy();
        using (var ext = Registry.CurrentUser.CreateSubKey(Classes + Extension))
        {
            ext.SetValue(string.Empty, ProgId);
            using var openWith = ext.CreateSubKey("OpenWithProgids");
            openWith.SetValue(ProgId, string.Empty);
        }

        using (var progId = Registry.CurrentUser.CreateSubKey(Classes + ProgId))
        {
            progId.SetValue(string.Empty, friendlyTypeName);
            using (var icon = progId.CreateSubKey("DefaultIcon"))
                icon.SetValue(string.Empty, $"\"{exePath}\",0");
            using var command = progId.CreateSubKey(@"shell\open\command");
            command.SetValue(string.Empty, CommandFor(exePath));
        }

        NotifyShell();
    }

    public static void Unregister()
    {
        RemoveLegacy();
        Registry.CurrentUser.DeleteSubKeyTree(Classes + ProgId, throwOnMissingSubKey: false);

        using (var ext = Registry.CurrentUser.OpenSubKey(Classes + Extension, writable: true))
        {
            if (ext is not null)
            {
                if (ext.GetValue(string.Empty) as string == ProgId)
                    ext.DeleteValue(string.Empty, throwOnMissingValue: false);
                using var openWith = ext.OpenSubKey("OpenWithProgids", writable: true);
                openWith?.DeleteValue(ProgId, throwOnMissingValue: false);
            }
        }

        NotifyShell();
    }

    /// <summary>
    /// Seule écriture silencieuse, au démarrage : l'utilisateur a déjà demandé l'association, mais le dossier de RSR
    /// a bougé depuis (version ZIP déplacée). Sans réparation, le double-clic lancerait un exécutable disparu.
    /// </summary>
    public static void RepairIfMoved(string exePath, string friendlyTypeName)
    {
        try
        {
            if (GetState(exePath) != PresetAssociationState.OtherExecutable) return;

            // Une autre copie de RSR bien présente (version installée à côté d'une version de développement) garde la main
            string? registered = RegisteredExecutable();
            if (registered is null || !System.IO.File.Exists(registered))
                Register(exePath, friendlyTypeName);
        }
        catch
        {
            // Confort seulement : ne jamais gêner le démarrage
        }
    }

    private static void RemoveLegacy()
    {
        Registry.CurrentUser.DeleteSubKeyTree(Classes + LegacyProgId, throwOnMissingSubKey: false);
        using var ext = Registry.CurrentUser.OpenSubKey(Classes + Extension, writable: true);
        if (ext is null) return;
        if (ext.GetValue(string.Empty) as string == LegacyProgId)
            ext.DeleteValue(string.Empty, throwOnMissingValue: false);
        using var openWith = ext.OpenSubKey("OpenWithProgids", writable: true);
        openWith?.DeleteValue(LegacyProgId, throwOnMissingValue: false);
    }

    /// <summary>Exécutable désigné par l'association de l'utilisateur, ou null. La commande a la forme "exe" "%1".</summary>
    internal static string? RegisteredExecutable()
    {
        using var cmd = Registry.CurrentUser.OpenSubKey(Classes + ProgId + @"\shell\open\command");
        return ExecutableOf(cmd?.GetValue(string.Empty) as string);
    }

    internal static string? ExecutableOf(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        command = command.Trim();
        if (command[0] != '"') return command.Split(' ')[0];
        int end = command.IndexOf('"', 1);
        return end > 1 ? command[1..end] : null;
    }

    private static void NotifyShell()
        => SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0, IntPtr.Zero, IntPtr.Zero);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
