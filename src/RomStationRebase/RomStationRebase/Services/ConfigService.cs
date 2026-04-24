using System.IO;
using System.Text.Json;
using RomStationRebase.Helpers;
using RomStationRebase.Models;

namespace RomStationRebase.Services;

/// <summary>
/// Lecture et écriture des fichiers JSON de configuration de RSRebase.
/// Emplacement : %LOCALAPPDATA%\RomStationRebase\
/// </summary>
public class ConfigService
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RomStationRebase");

    private static readonly string AppStateFile        = Path.Combine(ConfigDir, "app-state.json");
    private static readonly string UserPreferencesFile = Path.Combine(ConfigDir, "user-preferences.json");

    // Fichier de défauts distribué avec l'application — à côté de l'exécutable
    private static readonly string WindowDefaultsFile = Path.Combine(
        AppContext.BaseDirectory, "config", "window-defaults.json");

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    // ── AppState ──────────────────────────────────────────────────────────

    /// <summary>
    /// Charge app-state.json. Retourne un AppState vide si le fichier est absent.
    /// Lève ConfigCorruptedException si le JSON est malformé.
    /// </summary>
    public AppState LoadAppState()
    {
        if (!File.Exists(AppStateFile))
            return new AppState();

        try
        {
            string json = File.ReadAllText(AppStateFile);
            return JsonSerializer.Deserialize<AppState>(json) ?? new AppState();
        }
        catch (JsonException ex)
        {
            throw new ConfigCorruptedException("app-state.json", ex.Message);
        }
    }

    /// <summary>Sérialise AppState en JSON indenté et écrit le fichier (crée le dossier si nécessaire).</summary>
    public void SaveAppState(AppState state)
    {
        Directory.CreateDirectory(ConfigDir);
        File.WriteAllText(AppStateFile, JsonSerializer.Serialize(state, WriteOptions));
    }

    /// <summary>Supprime app-state.json s'il existe.</summary>
    public void DeleteAppState()
    {
        if (File.Exists(AppStateFile))
            File.Delete(AppStateFile);
    }

    // ── UserPreferences ───────────────────────────────────────────────────

    /// <summary>
    /// Charge user-preferences.json. Retourne les valeurs par défaut si le fichier est absent.
    /// Lève ConfigCorruptedException si le JSON est malformé ou si une valeur est invalide.
    /// </summary>
    public UserPreferences LoadUserPreferences()
    {
        if (!File.Exists(UserPreferencesFile))
            return new UserPreferences();

        try
        {
            string json = File.ReadAllText(UserPreferencesFile);
            var prefs = JsonSerializer.Deserialize<UserPreferences>(json) ?? new UserPreferences();
            ValidateUserPreferences(prefs);
            return prefs;
        }
        catch (JsonException ex)
        {
            throw new ConfigCorruptedException("user-preferences.json", ex.Message);
        }
    }

    /// <summary>Sérialise UserPreferences en JSON indenté et écrit le fichier.</summary>
    public void SaveUserPreferences(UserPreferences prefs)
    {
        Directory.CreateDirectory(ConfigDir);
        File.WriteAllText(UserPreferencesFile, JsonSerializer.Serialize(prefs, WriteOptions));
    }

    /// <summary>Supprime user-preferences.json s'il existe.</summary>
    public void DeleteUserPreferences()
    {
        if (File.Exists(UserPreferencesFile))
            File.Delete(UserPreferencesFile);
    }

    // ── Validation ────────────────────────────────────────────────────────

    private static readonly string[] ValidLanguages = { "auto", "en", "fr" };
    private static readonly string[] ValidPolicies  = { "Ignore", "Overwrite" };

    /// <summary>Vérifie que chaque champ de UserPreferences est dans la plage autorisée.</summary>
    private static void ValidateUserPreferences(UserPreferences prefs)
    {
        if (!ValidLanguages.Contains(prefs.AppLanguage))
            throw new ConfigCorruptedException("user-preferences.json",
                $"AppLanguage='{prefs.AppLanguage}' invalide (valeurs acceptées : auto, en, fr)");

        if (!ValidPolicies.Contains(prefs.DuplicatePolicy))
            throw new ConfigCorruptedException("user-preferences.json",
                $"DuplicatePolicy='{prefs.DuplicatePolicy}' invalide (valeurs acceptées : Ignore, Overwrite)");

        if (prefs.MaxParallelCopies < 1 || prefs.MaxParallelCopies > 16)
            throw new ConfigCorruptedException("user-preferences.json",
                $"MaxParallelCopies={prefs.MaxParallelCopies} hors plage (valeurs acceptées : 1 à 16)");

        if (prefs.LastViewMode != "Mosaic" && prefs.LastViewMode != "List")
            throw new ConfigCorruptedException("user-preferences.json",
                $"LastViewMode='{prefs.LastViewMode}' invalide (valeurs acceptées : Mosaic, List)");

        // Theme : normalisé en silence — préférence visuelle sans impact critique
        var validThemes = new[] { "Light", "Dark" };
        if (!validThemes.Contains(prefs.Theme))
            prefs.Theme = "Light";

        // ThumbnailSize : normalisé en silence plutôt que rejeté (préférence visuelle sans impact critique)
        if (prefs.ThumbnailSize != "Normal" && prefs.ThumbnailSize != "Grand")
            prefs.ThumbnailSize = "Normal";

        // LastSortCriteria : normalisé en silence — préférence visuelle sans impact critique
        if (prefs.LastSortCriteria != "Title" && prefs.LastSortCriteria != "System")
            prefs.LastSortCriteria = "Title";
    }

    // ── AppMetadata ───────────────────────────────────────────────────────

    /// <summary>
    /// Charge les métadonnées de l'application depuis config/app-metadata.json.
    /// En cas d'échec (fichier absent ou JSON invalide), retourne une AppMetadata
    /// avec des valeurs vides — l'UI doit savoir gérer ce cas sans planter.
    /// </summary>
    public AppMetadata LoadAppMetadata()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "config", "app-metadata.json");
            if (!File.Exists(path))
                return new AppMetadata();

            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppMetadata>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new AppMetadata();
        }
        catch
        {
            return new AppMetadata();
        }
    }

    // ── WindowDefaults ────────────────────────────────────────────────────

    /// <summary>
    /// Charge config/window-defaults.json (fichier distribué avec l'appli).
    /// Si le fichier est absent ou corrompu, utilise des valeurs codées en dur en fallback
    /// ET reconstruit le fichier pour la prochaine exécution (auto-réparation).
    /// Ne lève jamais d'exception : démarrage toujours garanti.
    /// </summary>
    public WindowDefaults LoadWindowDefaults()
    {
        // Tentative de lecture du fichier existant
        if (File.Exists(WindowDefaultsFile))
        {
            try
            {
                string json = File.ReadAllText(WindowDefaultsFile);
                var defaults = JsonSerializer.Deserialize<WindowDefaults>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (defaults is not null && IsValid(defaults))
                    return defaults;
            }
            catch
            {
                // Fichier corrompu — on tombe dans la reconstruction ci-dessous
            }
        }

        // Fallback : valeurs codées en dur + reconstruction du fichier
        var fallback = new WindowDefaults
        {
            MainWindow       = new WindowSize { Width = 1280, Height = 800  },
            RebaseWindow     = new WindowSize { Width = 1100, Height = 720  },
            GameDetailWindow = new WindowSize { Width = 700,  Height = 820  },
        };

        try
        {
            string? dir = Path.GetDirectoryName(WindowDefaultsFile);
            if (dir is not null) Directory.CreateDirectory(dir);
            File.WriteAllText(WindowDefaultsFile,
                JsonSerializer.Serialize(fallback, WriteOptions));
        }
        catch
        {
            // Si l'écriture échoue (disque plein, permissions...), on continue
            // silencieusement avec les valeurs en mémoire
        }

        return fallback;
    }

    /// <summary>Valide que les tailles sont dans une plage raisonnable (évite un JSON avec width=0).</summary>
    private static bool IsValid(WindowDefaults d)
        => d.MainWindow.Width        >= 400 && d.MainWindow.Height        >= 300
        && d.RebaseWindow.Width      >= 400 && d.RebaseWindow.Height      >= 300
        && d.GameDetailWindow.Width  >= 400 && d.GameDetailWindow.Height  >= 300;
}
