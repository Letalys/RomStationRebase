using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using RomStationRebase.Models;
using RomStationRebase.ViewModels;

namespace RomStationRebase.Services;

/// <summary>
/// Charge et écrit les architectures cibles depuis un seul dossier, celui de l'utilisateur :
/// %LOCALAPPDATA%\RomStationRebase\architectures\. Au premier lancement, les architectures par défaut
/// distribuées dans config/architectures/ y sont copiées ; les défauts ajoutés par une mise à jour le sont aussi,
/// une seule fois chacun. Le dossier d'installation n'est jamais écrit et sert de source pour
/// « Restaurer les architectures par défaut ».
/// </summary>
public class ArchitectureService
{
    private static readonly string ConfigDir =
        Path.Combine(AppContext.BaseDirectory, "config", "architectures");

    private static readonly string UserDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RomStationRebase", "architectures");

    private const string IndexFileName = "index.json";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly JsonSerializerOptions WriteOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Dossier des architectures de l'utilisateur — le seul lu par l'application.</summary>
    public static string UserArchitecturesDirectory => UserDir;

    // ── Chargement ────────────────────────────────────────────────────────

    /// <summary>
    /// Retourne les architectures du dossier utilisateur, après y avoir copié les défauts encore jamais copiés.
    /// Origine : Distributed si l'identifiant est un défaut distribué, Custom sinon.
    /// Lève une exception si l'index distribué est absent ou invalide.
    /// </summary>
    public List<ArchitectureEntry> LoadArchitectures()
    {
        EnsureSeeded();
        var shipped = LoadDistributedArchitectures().Select(a => a.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var user    = LoadUserIndex().Architectures;
        foreach (var a in user)
            a.Origin = shipped.Contains(a.Id) ? ArchitectureOrigin.Distributed : ArchitectureOrigin.Custom;
        return user;
    }

    /// <summary>Architectures par défaut telles que distribuées — source de la copie initiale et de la restauration.</summary>
    public List<ArchitectureEntry> LoadDistributedArchitectures()
    {
        string path = Path.Combine(ConfigDir, IndexFileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Fichier d'index des architectures introuvable : {path}");

        var index = JsonSerializer.Deserialize<ArchitectureIndex>(File.ReadAllText(path), JsonOpts)
            ?? throw new InvalidDataException("index.json est vide ou invalide.");
        foreach (var a in index.Architectures) a.Origin = ArchitectureOrigin.Distributed;
        return index.Architectures;
    }

    /// <summary>
    /// Lit le fichier de mapping dans le dossier utilisateur ; à défaut, dans les fichiers distribués
    /// (fichier effacé à la main). Lève une exception si le fichier est absent ou invalide.
    /// </summary>
    /// <param name="fileName">Nom du fichier de mapping (ex : "arkos_default.json").</param>
    public FolderTreeMapping LoadFolderTreeMapping(string fileName)
    {
        EnsureSeeded();
        string userPath = Path.Combine(UserDir, fileName);
        string path     = File.Exists(userPath) ? userPath : Path.Combine(ConfigDir, fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Fichier de mapping introuvable : {path}");

        return JsonSerializer.Deserialize<FolderTreeMapping>(File.ReadAllText(path), JsonOpts)
            ?? throw new InvalidDataException($"{fileName} est vide ou invalide.");
    }

    // ── Copie initiale des défauts ────────────────────────────────────────

    /// <summary>
    /// Copie dans le dossier utilisateur chaque architecture par défaut qui n'y a encore jamais été copiée
    /// (premier lancement, ou défaut ajouté par une mise à jour). Un défaut supprimé par l'utilisateur
    /// reste dans SeededDefaults et n'est donc pas recopié.
    /// </summary>
    private void EnsureSeeded()
    {
        var index   = LoadUserIndex();
        var shipped = LoadDistributedArchitectures();
        bool changed = false;

        foreach (var entry in shipped)
        {
            if (index.SeededDefaults.Contains(entry.Id, StringComparer.OrdinalIgnoreCase)) continue;

            CopyShippedMapping(entry.FolderTreeMapping, overwrite: false);
            if (!index.Architectures.Any(a => string.Equals(a.Id, entry.Id, StringComparison.OrdinalIgnoreCase)))
                index.Architectures.Add(entry);
            index.SeededDefaults.Add(entry.Id);
            changed = true;
        }

        if (changed) WriteUserIndex(index);
    }

    private static void CopyShippedMapping(string fileName, bool overwrite)
    {
        string source = Path.Combine(ConfigDir, fileName);
        string dest   = Path.Combine(UserDir, fileName);
        if (!File.Exists(source) || (File.Exists(dest) && !overwrite)) return;
        Directory.CreateDirectory(UserDir);
        File.Copy(source, dest, overwrite: true);
    }

    // ── Écriture ──────────────────────────────────────────────────────────

    /// <summary>Enregistre une architecture (nouvelle ou existante) : son mapping et son entrée dans l'index utilisateur.</summary>
    public void SaveUserArchitecture(ArchitectureEntry entry, FolderTreeMapping mapping)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Id);
        if (string.IsNullOrWhiteSpace(entry.FolderTreeMapping))
            entry.FolderTreeMapping = entry.Id + ".json";

        Directory.CreateDirectory(UserDir);
        File.WriteAllText(Path.Combine(UserDir, entry.FolderTreeMapping),
            JsonSerializer.Serialize(mapping, WriteOpts));

        var index = LoadUserIndex();
        int i = index.Architectures.FindIndex(a => string.Equals(a.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) index.Architectures[i] = entry; else index.Architectures.Add(entry);
        WriteUserIndex(index);
    }

    /// <summary>Retire une architecture du dossier utilisateur, défaut compris : entrée et fichier de mapping effacés.</summary>
    public void DeleteUserArchitecture(string id)
    {
        var index = LoadUserIndex();
        var entry = index.Architectures.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
        if (entry is null) return;

        index.Architectures.Remove(entry);
        WriteUserIndex(index);

        string mappingPath = Path.Combine(UserDir, entry.FolderTreeMapping);
        if (File.Exists(mappingPath))
            File.Delete(mappingPath);
    }

    /// <summary>
    /// Recopie toutes les architectures par défaut dans le dossier utilisateur : celles supprimées reviennent,
    /// celles modifiées reprennent leur contenu d'origine. Les architectures personnalisées sont conservées.
    /// </summary>
    public void RestoreDistributed()
    {
        var index   = LoadUserIndex();
        var shipped = LoadDistributedArchitectures();

        foreach (var entry in shipped)
        {
            CopyShippedMapping(entry.FolderTreeMapping, overwrite: true);
            int i = index.Architectures.FindIndex(a => string.Equals(a.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
            if (i >= 0) index.Architectures[i] = entry; else index.Architectures.Add(entry);
            if (!index.SeededDefaults.Contains(entry.Id, StringComparer.OrdinalIgnoreCase))
                index.SeededDefaults.Add(entry.Id);
        }
        WriteUserIndex(index);
    }

    /// <summary>True si au moins une architecture par défaut manque ou diffère de sa version distribuée.</summary>
    public bool HasDistributedChanges()
    {
        var index = LoadUserIndex();
        foreach (var entry in LoadDistributedArchitectures())
        {
            var user = index.Architectures.FirstOrDefault(a => string.Equals(a.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
            if (user is null) return true;
            if (JsonSerializer.Serialize(user, WriteOpts) != JsonSerializer.Serialize(entry, WriteOpts)) return true;

            string userPath = Path.Combine(UserDir, entry.FolderTreeMapping);
            string shipPath = Path.Combine(ConfigDir, entry.FolderTreeMapping);
            if (!File.Exists(userPath) || !File.Exists(shipPath)) return true;
            if (!File.ReadAllBytes(userPath).AsSpan().SequenceEqual(File.ReadAllBytes(shipPath))) return true;
        }
        return false;
    }

    private static ArchitectureIndex LoadUserIndex()
    {
        string path = Path.Combine(UserDir, IndexFileName);
        if (!File.Exists(path)) return new ArchitectureIndex();
        try
        {
            return JsonSerializer.Deserialize<ArchitectureIndex>(File.ReadAllText(path), JsonOpts) ?? new ArchitectureIndex();
        }
        catch (JsonException)
        {
            return new ArchitectureIndex(); // index illisible : on repart des défauts, les fichiers de mapping restent sur place
        }
    }

    private static void WriteUserIndex(ArchitectureIndex index)
    {
        Directory.CreateDirectory(UserDir);
        File.WriteAllText(Path.Combine(UserDir, IndexFileName), JsonSerializer.Serialize(index, WriteOpts));
    }

    /// <summary>Identifiant de fichier sûr dérivé d'un libellé : minuscules, lettres, chiffres et tirets.</summary>
    public static string MakeId(string label)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in label.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c)) sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        return sb.ToString().Trim('-');
    }

    // ── Résolution ────────────────────────────────────────────────────────

    /// <summary>
    /// Retourne le nom du dossier cible pour un système donné, ou null si aucun mapping.
    /// La comparaison est insensible à la casse.
    /// </summary>
    public string? GetTargetFolder(FolderTreeMapping mapping, string systemName)
        => mapping.FolderTreeMappings
            .FirstOrDefault(m => string.Equals(m.RomStationSystem, systemName,
                                               StringComparison.OrdinalIgnoreCase))
            ?.TargetFolder;

    /// <summary>
    /// Retourne les noms de systèmes des jeux sélectionnés qui n'ont pas de correspondance
    /// dans le mapping. Ces jeux seront ignorés lors du rebase.
    /// </summary>
    public List<string> GetUnmappedSystems(FolderTreeMapping mapping,
                                            List<GameItemViewModel> selectedGames)
        => selectedGames
            .Select(g => g.SystemName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(s => GetTargetFolder(mapping, s) is null)
            .OrderBy(s => s)
            .ToList();

    // ── Validation ────────────────────────────────────────────────────────

    /// <summary>
    /// Vérifie que le lecteur du chemin cible dispose d'au moins requiredBytes d'espace libre.
    /// </summary>
    public bool CheckDiskSpace(string targetPath, long requiredBytes)
    {
        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(targetPath));
            if (root is null) return true;
            var drive = new DriveInfo(root);
            return drive.AvailableFreeSpace >= requiredBytes;
        }
        catch
        {
            return true; // en cas de doute, on laisse passer
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Calcule le chemin absolu du dossier racine d'un jeu à partir de son GameDirectory Derby.
    /// GAME_DIRECTORY = chemin vers un fichier de jeu (ex: Games/Downloads/psx/Titre/disc1/rom.zip).
    /// Deux remontées donnent le dossier racine du jeu.
    /// </summary>
    internal static string ResolveGameRoot(string gameDirectory, string romStationPath)
    {
        string normalized = gameDirectory.Replace(':', '-');
        string? level1   = Path.GetDirectoryName(normalized);
        string? gameRoot = level1 != null ? Path.GetDirectoryName(level1) : null;
        return gameRoot != null
            ? Path.Combine(romStationPath, "app", gameRoot)
            : string.Empty;
    }
}
