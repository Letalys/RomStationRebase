using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using RomStationRebase.Models;
using RomStationRebase.Resources;

namespace RomStationRebase.Services;

/// <summary>
/// Outils externes de conversion : même modèle à dossier unique que les architectures. Tout vit dans
/// %LOCALAPPDATA%\RomStationRebase\tools\ (un JSON par outil) ; config/tools/ n'est que la source des défauts,
/// copiés une fois chacun et recopiés par « Restaurer les outils par défaut ». RSR ne distribue aucun exécutable :
/// l'utilisateur indique où se trouve le sien, outil par outil, et ce chemin vit dans l'index du dossier
/// (pas dans les préférences : plusieurs fenêtres en tiennent une copie en mémoire et se l'écraseraient).
/// </summary>
public class ExternalToolService
{
    private static readonly string ConfigDir = Path.Combine(AppContext.BaseDirectory, "config", "tools");

    private static readonly string UserDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RomStationRebase", "tools");

    private const string IndexFileName = "index.json";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented               = true,
        ReadCommentHandling         = JsonCommentHandling.Skip,
        AllowTrailingCommas         = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string UserToolsDirectory => UserDir;

    /// <summary>Dossier de travail des conversions : extraction et sortie de l'outil, sur le disque local, jamais sur la carte de destination.</summary>
    public static string WorkDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RomStationRebase", "work");

    // ── Chargement ────────────────────────────────────────────────────────

    /// <summary>Outils du dossier utilisateur, défauts semés au passage. Un fichier illisible est ignoré, jamais bloquant.</summary>
    public List<ExternalTool> LoadTools()
    {
        EnsureSeeded();
        var shipped = ShippedIds();
        var tools   = new List<ExternalTool>();

        if (!Directory.Exists(UserDir)) return tools;
        foreach (string file in Directory.EnumerateFiles(UserDir, "*.json"))
        {
            if (string.Equals(Path.GetFileName(file), IndexFileName, StringComparison.OrdinalIgnoreCase)) continue;
            var tool = TryRead(file);
            if (tool is null || string.IsNullOrWhiteSpace(tool.Id)) continue;
            tool.Origin = shipped.Contains(tool.Id) ? ArchitectureOrigin.Distributed : ArchitectureOrigin.Custom;
            tools.Add(tool);
        }
        return tools.OrderBy(t => t.Label, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static ExternalTool? TryRead(string file)
    {
        try { return JsonSerializer.Deserialize<ExternalTool>(File.ReadAllText(file), JsonOpts); }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { return null; }
    }

    private static HashSet<string> ShippedIds()
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(ConfigDir)) return ids;
        foreach (string file in Directory.EnumerateFiles(ConfigDir, "*.json"))
            ids.Add(Path.GetFileNameWithoutExtension(file));
        return ids;
    }

    /// <summary>Copie chaque outil par défaut une seule fois : un défaut supprimé par l'utilisateur n'est pas recopié au lancement suivant.</summary>
    public void EnsureSeeded()
    {
        try
        {
            if (!Directory.Exists(ConfigDir)) return;
            Directory.CreateDirectory(UserDir);
            var index   = LoadIndex();
            bool changed = false;
            foreach (string id in ShippedIds())
            {
                if (index.SeededDefaults.Contains(id, StringComparer.OrdinalIgnoreCase)) continue;
                string dest = Path.Combine(UserDir, id + ".json");
                if (!File.Exists(dest))
                    File.Copy(Path.Combine(ConfigDir, id + ".json"), dest);
                index.SeededDefaults.Add(id);
                changed = true;
            }
            if (changed) WriteIndex(index);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Dossier utilisateur inaccessible : les conversions seront simplement indisponibles
        }
    }

    // ── Ecriture ──────────────────────────────────────────────────────────

    public void Save(ExternalTool tool)
    {
        Directory.CreateDirectory(UserDir);
        string path = Path.Combine(UserDir, tool.Id + ".json");
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(tool, JsonOpts));
        File.Move(temp, path, overwrite: true);
    }

    public void Delete(string id)
    {
        string path = Path.Combine(UserDir, id + ".json");
        if (File.Exists(path)) File.Delete(path);
        SetExecutablePath(id, null); // l'emplacement appartient à l'outil : il part avec lui
    }

    /// <summary>Recopie tous les outils par défaut : les supprimés reviennent, les modifiés sont écrasés, les personnalisés restent.</summary>
    public void RestoreDefaults()
    {
        if (!Directory.Exists(ConfigDir)) return;
        Directory.CreateDirectory(UserDir);
        var index = LoadIndex();
        foreach (string id in ShippedIds())
        {
            File.Copy(Path.Combine(ConfigDir, id + ".json"), Path.Combine(UserDir, id + ".json"), overwrite: true);
            if (!index.SeededDefaults.Contains(id, StringComparer.OrdinalIgnoreCase))
                index.SeededDefaults.Add(id);
        }
        WriteIndex(index);
    }

    private static ExternalToolIndex LoadIndex()
    {
        string path = Path.Combine(UserDir, IndexFileName);
        if (!File.Exists(path)) return new ExternalToolIndex();
        try { return JsonSerializer.Deserialize<ExternalToolIndex>(File.ReadAllText(path), JsonOpts) ?? new ExternalToolIndex(); }
        catch (Exception ex) when (ex is JsonException or IOException) { return new ExternalToolIndex(); }
    }

    private static void WriteIndex(ExternalToolIndex index)
        => File.WriteAllText(Path.Combine(UserDir, IndexFileName), JsonSerializer.Serialize(index, JsonOpts));

    /// <summary>Identifiant de fichier sûr, tiré d'un libellé.</summary>
    public static string MakeId(string label, IEnumerable<string> existing)
    {
        string slug = Regex.Replace(label.Trim().ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        if (slug.Length == 0) slug = "outil";
        string id = slug;
        var taken = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        for (int n = 2; taken.Contains(id); n++) id = $"{slug}-{n}";
        return id;
    }

    // ── Exécutables ───────────────────────────────────────────────────────

    /// <summary>Emplacements indiqués par l'utilisateur, par identifiant d'outil.</summary>
    public Dictionary<string, string> LoadExecutablePaths()
        => new(LoadIndex().ExecutablePaths, StringComparer.OrdinalIgnoreCase);

    /// <summary>Retient (ou oublie, si <paramref name="path"/> est vide) l'emplacement de l'exécutable d'un outil.</summary>
    public void SetExecutablePath(string toolId, string? path)
    {
        if (string.IsNullOrWhiteSpace(toolId)) return;
        Directory.CreateDirectory(UserDir);
        var index = LoadIndex();
        if (string.IsNullOrWhiteSpace(path)) { if (!index.ExecutablePaths.Remove(toolId)) return; }
        else                                 index.ExecutablePaths[toolId] = path.Trim();
        WriteIndex(index);
    }

    /// <summary>
    /// Chemin de l'exécutable d'un outil parmi ceux que l'utilisateur a indiqués, ou null s'il manque ou n'existe plus.
    /// Seul un chemin choisi par l'utilisateur est utilisé : RSR ne lance jamais un programme qu'il aurait trouvé tout seul.
    /// </summary>
    public static string? ResolveExecutable(ExternalTool tool, IReadOnlyDictionary<string, string> paths)
    {
        if (!paths.TryGetValue(tool.Id, out string? path) || string.IsNullOrWhiteSpace(path)) return null;
        return File.Exists(path) ? path : FollowEmulatorUpdate(path);
    }

    // ── Emulateurs installés par RomStation ───────────────────────────────
    // RomStation range ses émulateurs sous app\emulators\downloads\<Emulateur>\files\<Emulateur version (arch)>\ :
    // son MAME contient chdman.exe, son Dolphin contient DolphinTool.exe.

    private const string EmulatorsMarker = @"\emulators\downloads\";

    /// <summary>
    /// Exécutable trouvé dans les émulateurs installés par RomStation. A PROPOSER, comme Detect.
    /// <paramref name="emulatorDirectories"/> : les dossiers que RomStation enregistre dans sa base
    /// (DerbyService.GetEmulatorDirectories), version la plus récente en premier — c'est la source qui fait foi.
    /// Sans eux (base illisible), on se rabat sur l'arborescence connue du dossier des émulateurs.
    /// </summary>
    public static string? DetectInRomStation(ExternalTool tool, string? romStationPath, IReadOnlyList<string>? emulatorDirectories = null)
    {
        if (string.IsNullOrWhiteSpace(tool.Executable) || string.IsNullOrWhiteSpace(romStationPath)) return null;
        string name = Path.GetFileName(tool.Executable.Trim());
        try
        {
            if (emulatorDirectories is { Count: > 0 })
            {
                string app = Path.Combine(romStationPath, "app");
                foreach (string directory in emulatorDirectories)
                {
                    // Path.Combine garde un chemin absolu tel quel : émulateur ajouté à la main, hors du dossier de RomStation
                    string candidate = Path.Combine(app, directory.Replace('/', Path.DirectorySeparatorChar), name);
                    if (File.Exists(candidate)) return Path.GetFullPath(candidate);
                }
                return null;
            }

            string root = Path.Combine(romStationPath, "app", "emulators", "downloads");
            if (!Directory.Exists(root)) return null;
            // Profondeur fixe : surtout pas de parcours récursif, le dossier de MAME contient des milliers de fichiers
            return Directory.EnumerateDirectories(root)
                .Select(emulator => Path.Combine(emulator, "files"))
                .Where(Directory.Exists)
                .SelectMany(Directory.EnumerateDirectories)
                .Select(version => Path.Combine(version, name))
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    /// <summary>True si ce chemin désigne un fichier d'un émulateur installé par RomStation.</summary>
    public static bool IsRomStationEmulatorPath(string? path)
        => !string.IsNullOrEmpty(path) && path.Contains(EmulatorsMarker, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// RomStation met ses émulateurs à jour en changeant de dossier (« MAME 0.271 (x64) » devient « MAME 0.272 (x64) ») :
    /// l'emplacement choisi par l'utilisateur disparaît alors. Son choix était « le chdman du MAME de RomStation » :
    /// on le suit dans le dossier de version voisin, du même émulateur, et nulle part ailleurs.
    /// </summary>
    internal static string? FollowEmulatorUpdate(string missingPath)
    {
        try
        {
            if (!IsRomStationEmulatorPath(missingPath)) return null;
            string? versionDir = Path.GetDirectoryName(missingPath);
            string? filesDir   = Path.GetDirectoryName(versionDir);
            if (filesDir is null || !Directory.Exists(filesDir)
                || !string.Equals(Path.GetFileName(filesDir), "files", StringComparison.OrdinalIgnoreCase))
                return null;

            string name = Path.GetFileName(missingPath);
            return Directory.EnumerateDirectories(filesDir)
                .Select(version => Path.Combine(version, name))
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    /// <summary>Emplacement probable de l'exécutable, à PROPOSER à l'utilisateur : émulateurs de RomStation d'abord, puis dossiers du descripteur, dossier bin de RSR, PATH.</summary>
    public static string? Detect(ExternalTool tool, string? romStationPath = null, IReadOnlyList<string>? emulatorDirectories = null)
    {
        if (string.IsNullOrWhiteSpace(tool.Executable)) return null;
        if (DetectInRomStation(tool, romStationPath, emulatorDirectories) is { } fromRomStation) return fromRomStation;
        string name = Path.GetFileName(tool.Executable.Trim());

        var folders = new List<string>();
        folders.AddRange(tool.SearchPaths.Select(Environment.ExpandEnvironmentVariables));
        folders.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RomStationRebase", "bin"));
        folders.AddRange((Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries));

        foreach (string folder in folders)
        {
            try
            {
                string candidate = Path.Combine(folder.Trim().Trim('"'), name);
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { /* entrée de PATH invalide */ }
        }
        return null;
    }

    /// <summary>Bouton Tester : lance l'outil avec ses arguments de version, lit la version et la compare au minimum exigé.</summary>
    public static async Task<ExternalToolTestResult> TestAsync(ExternalTool tool, string executablePath, CancellationToken ct = default)
    {
        if (!File.Exists(executablePath))
            return new ExternalToolTestResult(false, null, string.Format(Strings.Tools_Test_NotFound, executablePath));

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            var result = await ExternalToolRunner.RunAsync(executablePath, tool.VersionArguments, null,
                "both", null, null, 15, timeout.Token).ConfigureAwait(false);

            string? version = null;
            if (!string.IsNullOrWhiteSpace(tool.VersionRegex))
            {
                try
                {
                    var m = Regex.Match(result.Output, tool.VersionRegex, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
                    if (m.Success && m.Groups.Count > 1) version = m.Groups[1].Value;
                }
                catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException) { /* regex du descripteur invalide */ }
            }

            if (!string.IsNullOrWhiteSpace(tool.MinVersion) && version is not null && CompareVersions(version, tool.MinVersion) < 0)
                return new ExternalToolTestResult(false, version, string.Format(Strings.Tools_Test_TooOld, version, tool.MinVersion));

            return new ExternalToolTestResult(true, version,
                version is null ? Strings.Tools_Test_Answers : string.Format(Strings.Tools_Test_Version, version));
        }
        catch (OperationCanceledException)
        {
            return new ExternalToolTestResult(false, null, Strings.Tools_Test_Timeout);
        }
        catch (ExternalToolException ex)
        {
            return new ExternalToolTestResult(false, null, ex.Message);
        }
    }

    /// <summary>Compare deux versions à points ("0.230" et "0.255", "1.12.0" et "1.9") segment par segment, en nombres.</summary>
    internal static int CompareVersions(string a, string b)
    {
        int[] pa = Parse(a), pb = Parse(b);
        for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
        {
            int x = i < pa.Length ? pa[i] : 0, y = i < pb.Length ? pb[i] : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;

        static int[] Parse(string v) => Regex.Matches(v, @"\d+").Select(m => int.TryParse(m.Value, out int n) ? n : 0).ToArray();
    }
}
