using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using RomStationRebase.Models;
using RomStationRebase.ViewModels;

namespace RomStationRebase.Services;

/// <summary>
/// Charge les architectures cibles depuis config/architectures/ et fournit
/// les utilitaires de validation et d'estimation nécessaires avant un rebase.
/// </summary>
public class ArchitectureService
{
    private static readonly string ConfigDir =
        Path.Combine(AppContext.BaseDirectory, "config", "architectures");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    // ── Chargement ────────────────────────────────────────────────────────

    /// <summary>
    /// Lit config/architectures/index.json et retourne la liste des architectures disponibles.
    /// Lève une exception si le fichier est absent ou invalide.
    /// </summary>
    public List<ArchitectureEntry> LoadArchitectures()
    {
        string path = Path.Combine(ConfigDir, "index.json");
        if (!File.Exists(path))
            throw new FileNotFoundException($"Fichier d'index des architectures introuvable : {path}");

        var index = JsonSerializer.Deserialize<ArchitectureIndex>(File.ReadAllText(path), JsonOpts)
            ?? throw new InvalidDataException("index.json est vide ou invalide.");

        return index.Architectures;
    }

    /// <summary>
    /// Lit config/architectures/{fileName} et retourne la table de mapping.
    /// Lève une exception si le fichier est absent ou invalide.
    /// </summary>
    /// <param name="fileName">Nom du fichier de mapping (ex : "retroarch.json").</param>
    public FolderTreeMapping LoadFolderTreeMapping(string fileName)
    {
        string path = Path.Combine(ConfigDir, fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Fichier de mapping introuvable : {path}");

        return JsonSerializer.Deserialize<FolderTreeMapping>(File.ReadAllText(path), JsonOpts)
            ?? throw new InvalidDataException($"{fileName} est vide ou invalide.");
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
    /// Vérifie que le dernier segment du chemin cible ne contient pas
    /// de caractères interdits par Windows.
    /// </summary>
    public bool ValidateTargetPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        char[] invalid = Path.GetInvalidPathChars();
        return !path.Any(c => invalid.Contains(c));
    }

    /// <summary>
    /// Estime la taille totale (en octets) des fichiers sources des jeux sélectionnés.
    /// Parcourt récursivement le dossier racine de chaque jeu dans l'installation RomStation.
    /// </summary>
    public long EstimateTotalSize(List<GameItemViewModel> selectedGames, string romStationPath)
    {
        long total = 0;
        foreach (var game in selectedGames)
        {
            if (string.IsNullOrEmpty(game.GameDirectory)) continue;
            string gameRoot = ResolveGameRoot(game.GameDirectory, romStationPath);
            if (!Directory.Exists(gameRoot)) continue;
            total += Directory
                .GetFiles(gameRoot, "*", SearchOption.AllDirectories)
                .Sum(f => new FileInfo(f).Length);
        }
        return total;
    }

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
