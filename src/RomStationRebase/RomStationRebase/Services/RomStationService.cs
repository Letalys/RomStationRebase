using java.sql;
using Microsoft.Win32;
using org.apache.derby.jdbc;
using RomStationRebase.Resources;
using SystemFile      = System.IO.File;
using SystemPath      = System.IO.Path;
using SystemDirectory = System.IO.Directory;

namespace RomStationRebase.Services;

/// <summary>
/// Détection, validation et préparation de l'installation RomStation.
/// Couvre la lecture du registre, le parsing de RomStation.cfg,
/// la copie de la base Derby et le test de connexion via IKVM.
/// </summary>
public class RomStationService
{
    private const string UninstallKey   = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string UninstallKey32 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string DatabaseSubPath = @"app\database";
    private const string ConfigSubPath   = @"app\RomStation.cfg";
    private const string LockFileName    = "db.lck";

    // ── Détection dans le registre ────────────────────────────────────────

    /// <summary>
    /// Cherche RomStation dans HKLM Uninstall (vue 64-bit et 32-bit).
    /// Retourne InstallLocation si trouvé, null sinon.
    /// </summary>
    public string? DetectRomStationPath()
        => SearchUninstallKey(RegistryView.Registry64, UninstallKey)
        ?? SearchUninstallKey(RegistryView.Registry64, UninstallKey32)
        ?? SearchUninstallKey(RegistryView.Registry32, UninstallKey);

    private static string? SearchUninstallKey(RegistryView view, string keyPath)
    {
        using var hklm     = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
        using var uninstall = hklm.OpenSubKey(keyPath);
        if (uninstall is null) return null;

        foreach (string name in uninstall.GetSubKeyNames())
        {
            using var sub = uninstall.OpenSubKey(name);
            if (sub is null) continue;

            string? displayName = sub.GetValue("DisplayName") as string;
            if (displayName is null || !displayName.Contains("RomStation", StringComparison.OrdinalIgnoreCase))
                continue;

            string? location = sub.GetValue("InstallLocation") as string;
            if (!string.IsNullOrWhiteSpace(location))
                return location.TrimEnd('\\', '/');
        }
        return null;
    }

    // ── Validation du dossier ─────────────────────────────────────────────

    /// <summary>
    /// Vérifie que le dossier contient bien app/database et app/RomStation.cfg.
    /// </summary>
    public bool ValidateRomStationPath(string path)
        => SystemDirectory.Exists(path)
        && SystemDirectory.Exists(SystemPath.Combine(path, DatabaseSubPath))
        && SystemFile.Exists(SystemPath.Combine(path, ConfigSubPath));

    // ── Lecture de la configuration INI ──────────────────────────────────

    /// <summary>
    /// Lit app/RomStation.cfg (format clé=valeur).
    /// Extrait app.version et la version Derby depuis app.classpath.
    /// Retourne "unknown" pour chaque valeur absente.
    /// </summary>
    public (string version, string derbyVersion) ParseRomStationConfig(string romStationPath)
    {
        string cfgPath   = SystemPath.Combine(romStationPath, ConfigSubPath);
        string version   = "unknown";
        string derbyVer  = "unknown";

        foreach (string line in SystemFile.ReadAllLines(cfgPath))
        {
            int eq = line.IndexOf('=');
            if (eq < 0) continue;
            string key = line[..eq].Trim();
            string val = line[(eq + 1)..].Trim();

            if (key == "app.version")
                version = val;

            // app.classpath : tokens séparés par ';', cherche "derby-*.jar"
            if (key == "app.classpath")
            {
                foreach (string token in val.Split(';'))
                {
                    string fn = SystemPath.GetFileName(token.Trim());
                    if (fn.StartsWith("derby-", StringComparison.OrdinalIgnoreCase)
                     && fn.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                    {
                        // "derby-10.14.2.0.jar" → "10.14.2.0"
                        derbyVer = fn[6..].Replace(".jar", string.Empty);
                    }
                }
            }
        }

        return (version, derbyVer);
    }

    // ── Verrou de la base ─────────────────────────────────────────────────

    /// <summary>Retourne true si db.lck existe dans app/database (RomStation est ouvert).</summary>
    public bool IsDatabaseLocked(string romStationPath)
        => SystemFile.Exists(SystemPath.Combine(romStationPath, DatabaseSubPath, LockFileName));

    // ── Copie de la base ──────────────────────────────────────────────────

    /// <summary>
    /// Copie récursivement app/database vers destinationPath.
    /// Supprime db.lck dans la copie pour permettre la connexion Derby.
    /// Rapporte la progression via IProgress.
    /// </summary>
    public async Task<bool> CopyDatabaseAsync(string romStationPath, string destinationPath,
        IProgress<string> progress)
    {
        string sourcePath = SystemPath.Combine(romStationPath, DatabaseSubPath);

        // Suppression du dossier destination — peut échouer si un outil externe (DBeaver, etc.)
        // verrouille un fichier Derby dans la copie précédente.
        try
        {
            progress.Report("Preparing destination...");
            await Task.Run(() =>
            {
                if (SystemDirectory.Exists(destinationPath))
                    SystemDirectory.Delete(destinationPath, recursive: true);
            });
        }
        catch (System.IO.IOException ioEx)
        {
            Console.WriteLine($"[RomStationService] Impossible de supprimer la destination (verrou externe) : {ioEx.Message}");
            progress.Report(Strings.Splash_DBLocked);
            return false;
        }

        try
        {

            progress.Report("Copying files...");
            await Task.Run(() => CopyDirectoryRecursive(sourcePath, destinationPath));

            // Supprime le verrou dans la copie — Derby refuserait de s'y connecter
            string lockCopy = SystemPath.Combine(destinationPath, LockFileName);
            if (SystemFile.Exists(lockCopy))
                SystemFile.Delete(lockCopy);

            progress.Report("Copy complete.");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RomStationService] Erreur copie base : {ex.Message}");
            return false;
        }
    }

    private static void CopyDirectoryRecursive(string source, string destination)
    {
        SystemDirectory.CreateDirectory(destination);
        foreach (string file in SystemDirectory.GetFiles(source))
            SystemFile.Copy(file,
                SystemPath.Combine(destination, SystemPath.GetFileName(file)),
                overwrite: true);
        foreach (string subDir in SystemDirectory.GetDirectories(source))
            CopyDirectoryRecursive(subDir,
                SystemPath.Combine(destination, SystemPath.GetFileName(subDir)));
    }

    // ── Test de connexion ─────────────────────────────────────────────────

    /// <summary>
    /// Tente une connexion Derby sur dbCopyPath et exécute SELECT COUNT(*) FROM APP.GAME.
    /// Retourne true si la base répond correctement, false si exception.
    /// </summary>
    public async Task<bool> TestDatabaseConnectionAsync(string dbCopyPath)
    {
        return await Task.Run(() =>
        {
            try
            {
                // Enregistrement du driver Derby embarqué via IKVM
                java.lang.Class.forName("org.apache.derby.jdbc.EmbeddedDriver");
                DriverManager.registerDriver(new EmbeddedDriver());

                // Derby attend des slashes Unix dans l'URL JDBC
                string jdbcUrl = $"jdbc:derby:{dbCopyPath.Replace('\\', '/')}";
                var conn = DriverManager.getConnection(jdbcUrl);

                var stmt = conn.createStatement();
                var rs   = stmt.executeQuery("SELECT COUNT(*) FROM APP.GAME");
                rs.next();
                int count = rs.getInt(1);
                rs.close();
                stmt.close();
                conn.close();

                Console.WriteLine($"[RomStationService] Test DB : {count} jeux trouvés.");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RomStationService] Test DB échoué : {ex.Message}");
                return false;
            }
        });
    }
}
