using System.IO;
using System.Text.RegularExpressions;
using RomStationRebase.Models;

namespace RomStationRebase.Services;

/// <summary>
/// Jeu candidat au plan : le strict nécessaire, sans dépendance aux ViewModels.
/// Les trois surcharges, null par défaut, remplacent pour ce seul jeu les marqueurs du système dans l'architecture.
/// </summary>
public sealed record PlanGameInput(
    int     Id,
    string  Title,
    string  SystemName,
    string? SystemImagePath,
    int     Rid,
    string? CoverPath,
    bool    CoverExists,
    bool?   KeepFileNameOverride = null,
    bool?   M3UOverride          = null,
    bool?   ExtractOverride      = null);

/// <summary>Entrées du planificateur : jeux, fichiers Derby, architecture et options de sortie.</summary>
public sealed class RebasePlanRequest
{
    /// <summary>Jeux cochés pour le rebase.</summary>
    public required IReadOnlyList<PlanGameInput> SelectedGames { get; init; }

    /// <summary>Toute la bibliothèque — les homonymes se départagent sur l'ensemble, pour des noms stables d'un rebase à l'autre.</summary>
    public required IReadOnlyList<PlanGameInput> AllGames { get; init; }

    /// <summary>Fichiers Derby de chaque jeu (toute la bibliothèque).</summary>
    public required IReadOnlyDictionary<int, IReadOnlyList<GameFileInfo>> FilesByGame { get; init; }

    /// <summary>Inspection d'une archive source, null si inconnue (elle sera alors copiée telle quelle).</summary>
    public required Func<string, ArchiveInfo?> ArchiveLookup { get; init; }

    /// <summary>Taille d'un fichier source, 0 s'il est inaccessible.</summary>
    public required Func<string, long> FileSizeLookup { get; init; }

    /// <summary>Fichiers d'un dossier source, récursivement : chemin relatif au dossier (séparateur "/") et taille.</summary>
    public required Func<string, IReadOnlyList<(string RelativePath, long Size)>> DirectoryFilesLookup { get; init; }

    public required string            RomStationPath { get; init; }
    public required FolderTreeMapping Mapping        { get; init; }
    public required ArchitectureEntry Architecture   { get; init; }

    public bool          GenerateM3U      { get; init; }
    public ArchiveMode   ArchiveMode      { get; init; }
    public ExtractLayout Layout           { get; init; }
    public bool          CopyCovers       { get; init; }
    public bool          GenerateGamelist { get; init; }
}

/// <summary>
/// Calcule une fois pour toutes le nom et l'emplacement cible de chaque fichier d'un rebase.
/// Copie, M3U, jaquettes et gamelist s'appuient tous sur ce plan — jamais sur un nommage recalculé ailleurs.
/// Règles : romsets arcade conservés, disques lus dans le libellé Derby, homonymes départagés, extraction par système.
/// </summary>
public class RebasePlanner
{
    private static readonly Regex DiscMarker = new(
        @"\s*\((?:Disc|Disque|Disk|CD)\s*(\d+)\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Extensions candidates au rôle de fichier principal d'une archive extraite, par priorité décroissante.</summary>
    private static readonly string[] MainEntryPriority =
        [".cue", ".gdi", ".ccd", ".toc", ".chd", ".m3u", ".iso", ".cso", ".pbp", ".rvz", ".gcm", ".img", ".bin"];

    /// <summary>Marqueur du dossier des jaquettes : remplacé par le dossier système, et le chemin part alors de la destination.</summary>
    public const string SystemToken = "{system}";

    /// <summary>En dessous de 16 Mo, un .bin seul est une ROM de cartouche, pas une image disque.</summary>
    internal const long DiscImageMinBytes = 16L * 1024 * 1024;

    // ── Point d'entrée ────────────────────────────────────────────────────

    public RebasePlan Plan(RebasePlanRequest request)
    {
        var baseNames = ComputeBaseNames(request);
        var games     = new List<RebaseGamePlan>(request.SelectedGames.Count);

        foreach (var game in request.SelectedGames)
            games.Add(PlanGame(game, baseNames[game.Id], request));

        return new RebasePlan { Games = games };
    }

    // ── Noms de base et homonymes ─────────────────────────────────────────

    /// <summary>
    /// Nom de base de chaque jeu de la bibliothèque, unique par système.
    /// 1. Titre nettoyé. 2. Homonymes : libellé Derby du premier fichier s'il les distingue.
    /// 3. Sinon suffixe "(RS-rid)", l'identifiant RomStation, stable et garanti unique.
    /// </summary>
    internal static Dictionary<int, string> ComputeBaseNames(RebasePlanRequest request)
    {
        var names = request.AllGames.ToDictionary(g => g.Id, g => SanitizeName(g.Title));

        // Les jeux cochés doivent toujours avoir un nom, même s'ils manquent dans AllGames
        foreach (var g in request.SelectedGames)
            names.TryAdd(g.Id, SanitizeName(g.Title));

        var all = request.AllGames.Concat(request.SelectedGames)
            .GroupBy(g => g.Id).Select(x => x.First()).ToList();

        // Passe 2 : libellé Derby pour les groupes d'homonymes
        foreach (var group in CollisionGroups(all, names))
        {
            var candidates = group.ToDictionary(g => g.Id, g =>
            {
                var label = request.FilesByGame.TryGetValue(g.Id, out var files) && files.Count > 0
                    ? StripDiscMarker(files[0].Label)
                    : string.Empty;
                return SanitizeName(label);
            });

            bool allDistinct = candidates.Values.All(c => c.Length > 0)
                && candidates.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count() == candidates.Count;

            if (allDistinct)
                foreach (var (id, candidate) in candidates)
                    names[id] = candidate;
        }

        // Passe 3 : tout ce qui entre encore en collision reçoit l'identifiant RomStation
        foreach (var group in CollisionGroups(all, names))
            foreach (var g in group)
                names[g.Id] = $"{SanitizeName(g.Title)} (RS-{g.Rid})";

        return names;
    }

    private static IEnumerable<List<PlanGameInput>> CollisionGroups(
        List<PlanGameInput> games, Dictionary<int, string> names)
        => games
            .GroupBy(g => (System: g.SystemName.ToLowerInvariant(), Name: names[g.Id].ToLowerInvariant()))
            .Where(grp => grp.Count() > 1)
            .Select(grp => grp.ToList())
            .ToList();

    // ── Plan d'un jeu ─────────────────────────────────────────────────────

    private static RebaseGamePlan PlanGame(PlanGameInput game, string baseName, RebasePlanRequest req)
    {
        var mapping = req.Mapping.FolderTreeMappings.FirstOrDefault(m =>
            string.Equals(m.RomStationSystem, game.SystemName, StringComparison.OrdinalIgnoreCase));

        var common = new RebaseGamePlan
        {
            GameId          = game.Id,
            Title           = game.Title,
            SystemName      = game.SystemName,
            SystemImagePath = game.SystemImagePath,
            BaseName        = baseName,
        };

        if (mapping is null)
            return new RebaseGamePlan
            {
                GameId = game.Id, Title = game.Title, SystemName = game.SystemName,
                SystemImagePath = game.SystemImagePath, BaseName = baseName,
                TargetFolder = null, OutputKind = GameOutputKind.Unmapped,
            };

        // Règles effectives : l'architecture, sauf surcharge posée pour ce jeu dans la fenêtre de rebase
        bool keepFileName    = game.KeepFileNameOverride ?? mapping.KeepFileName;
        bool m3uSupported    = game.M3UOverride          ?? mapping.M3U;
        bool extractRequired = game.ExtractOverride      ?? mapping.Extract;

        req.FilesByGame.TryGetValue(game.Id, out var dbFiles);
        var sources = (dbFiles ?? []).ToList();

        // Disques : chaque fichier porte un numéro distinct dans son libellé
        var discNumbers = sources.Select(f => DiscNumber(f.Label)).ToList();
        bool isDiscSet  = sources.Count > 1
            && discNumbers.All(n => n.HasValue)
            && discNumbers.Distinct().Count() == sources.Count;

        var order = isDiscSet
            ? sources.Select((f, i) => (f, n: discNumbers[i]!.Value)).OrderBy(x => x.n).Select(x => x.f).ToList()
            : sources;

        var stems = TargetStems(order, baseName, isDiscSet, keepFileName);
        var files = new List<RebaseFilePlan>(order.Count);
        bool anyExtract = false, isFolder = false;

        for (int i = 0; i < order.Count; i++)
        {
            var plan = PlanFile(order[i], stems[i], keepFileName, extractRequired, req);
            anyExtract |= plan.Kind == FileTransferKind.Extract;
            isFolder   |= plan.Kind == FileTransferKind.CopyTree;
            files.Add(plan);
        }

        // M3U : seulement de vrais disques, sur un système dont l'émulateur le lit, jamais pour un romset
        bool withM3U = req.GenerateM3U && m3uSupported && isDiscSet && !keepFileName;
        string? m3uPath = withM3U ? baseName + ".m3u" : null;
        var m3uEntries  = withM3U ? files.Select(f => f.LaunchRelativePath).ToList() : [];

        // Entrées gamelist : une par élément lançable
        var entries = new List<GamelistEntryPlan>();
        var covers  = new List<RebaseCoverPlan>();
        bool coversWanted = req.CopyCovers && game.CoverExists && !string.IsNullOrEmpty(game.CoverPath);

        // Dossier des jaquettes : relatif au dossier système, ou à la destination s'il porte le marqueur {system}
        // (ES-DE/downloaded_media/{system}/covers, MUOS/info/catalogue/{system}/box…)
        string format      = req.Architecture.GamelistFormat ?? string.Empty;
        string coverFolder = req.Architecture.CoverFolder.Replace('\\', '/').Trim('/');
        bool   coversAtRoot = coverFolder.Contains(SystemToken, StringComparison.OrdinalIgnoreCase);
        if (coversAtRoot)
            coverFolder = coverFolder.Replace(SystemToken, mapping.TargetFolder.Replace('\\', '/').Trim('/'),
                StringComparison.OrdinalIgnoreCase);

        string CoverRel(string stem)
            => coverFolder.Length == 0
                ? $"{stem}{req.Architecture.CoverSuffix}.png"
                : $"{coverFolder}/{stem}{req.Architecture.CoverSuffix}.png";

        // Balise image : seulement pour un format qui la porte. Hors du dossier système, on remonte d'autant de niveaux.
        string? ImageTag(string? coverRel)
        {
            if (coverRel is null || !MetadataFormats.WritesImage(format)) return null;
            if (!coversAtRoot) return "./" + coverRel;
            int depth = mapping.TargetFolder.Replace('\\', '/').Trim('/').Split('/').Length;
            return string.Concat(Enumerable.Repeat("../", depth)) + coverRel;
        }

        void AddCover(string coverRel)
        {
            if (!covers.Any(c => string.Equals(c.DestRelativePath, coverRel, StringComparison.OrdinalIgnoreCase)))
                covers.Add(new RebaseCoverPlan { SourcePath = game.CoverPath!, DestRelativePath = coverRel, IsRootRelative = coversAtRoot });
        }

        bool mirror = MetadataFormats.MirrorsLaunchPath(format);

        if (withM3U)
        {
            string? img = coversWanted ? CoverRel(baseName) : null;
            entries.Add(new GamelistEntryPlan { Path = "./" + m3uPath, Name = game.Title, Image = ImageTag(img) });
            if (img is not null) AddCover(img);
        }
        else
        {
            for (int i = 0; i < files.Count; i++)
            {
                var f = files[i];
                string stem = mirror ? WithoutExtension(f.LaunchRelativePath) : CoverStem(f, stems[i]);
                string? img = coversWanted ? CoverRel(stem) : null;
                entries.Add(new GamelistEntryPlan
                {
                    Path  = "./" + f.LaunchRelativePath,
                    Name  = files.Count == 1 || keepFileName ? game.Title : f.Label,
                    Image = ImageTag(img),
                });
                if (img is not null) AddCover(img);
            }
        }

        if (!req.GenerateGamelist || !req.Architecture.SupportsGamelist)
            entries.Clear();

        long bytes = files.Sum(f => f.PlannedBytes)
                   + covers.Sum(c => req.FileSizeLookup(c.SourcePath));

        var kind = files.Count == 0        ? GameOutputKind.Copy
                 : isFolder                ? GameOutputKind.Folder
                 : keepFileName            ? GameOutputKind.Romset
                 : withM3U                 ? GameOutputKind.DiscSet
                 : anyExtract              ? GameOutputKind.Extract
                 : isDiscSet               ? GameOutputKind.Discs
                 : files.Count > 1         ? GameOutputKind.Variants
                 :                           GameOutputKind.Copy;

        var outputs = new List<string>();
        foreach (var f in files)
        {
            outputs.Add(f.DestRelativeDir.Length > 0
                ? $"{mapping.TargetFolder}/{f.DestRelativeDir}/"
                : $"{mapping.TargetFolder}/{f.LaunchRelativePath}");
            // .cue généré à plat : le .bin qu'il décrit est écrit à côté de lui
            if (f.CueBinRelativePath is not null && f.DestRelativeDir.Length == 0)
                outputs.Add($"{mapping.TargetFolder}/{f.CueBinRelativePath}");
        }
        if (m3uPath is not null) outputs.Add($"{mapping.TargetFolder}/{m3uPath}");
        foreach (var c in covers)
            outputs.Add(c.IsRootRelative ? c.DestRelativePath : $"{mapping.TargetFolder}/{c.DestRelativePath}");

        return new RebaseGamePlan
        {
            GameId          = common.GameId,
            Title           = common.Title,
            SystemName      = common.SystemName,
            SystemImagePath = common.SystemImagePath,
            BaseName        = baseName,
            TargetFolder    = mapping.TargetFolder,
            OutputKind      = kind,
            Files           = files,
            M3URelativePath = m3uPath,
            M3UEntries      = m3uEntries,
            Covers          = covers,
            GamelistEntries = entries,
            PlannedBytes    = bytes,
            OutputPaths     = outputs.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        };
    }

    /// <summary>Nom cible (sans extension) de chaque fichier d'un jeu, unique au sein du jeu.</summary>
    private static List<string> TargetStems(List<GameFileInfo> files, string baseName, bool isDiscSet, bool keepFileName)
    {
        var stems = new List<string>(files.Count);
        for (int i = 0; i < files.Count; i++)
        {
            string stem;
            if (keepFileName)
                stem = Path.GetFileNameWithoutExtension(files[i].RelativePath);
            else if (files.Count == 1)
                stem = baseName;
            else if (isDiscSet)
                stem = $"{baseName} (Disc {DiscNumber(files[i].Label)})";
            else
            {
                stem = SanitizeName(files[i].Label);
                if (stem.Length == 0) stem = $"{baseName} ({i + 1})";
            }

            // Deux fichiers du même jeu ne peuvent pas viser le même nom
            string candidate = stem;
            int n = 2;
            while (stems.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                candidate = $"{stem} ({n++})";
            stems.Add(candidate);
        }
        return stems;
    }

    private static RebaseFilePlan PlanFile(GameFileInfo file, string stem, bool keepFileName, bool extractRequired, RebasePlanRequest req)
    {
        string source    = Path.Combine(req.RomStationPath, "app", file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        string sourceDir = Path.GetDirectoryName(source) ?? string.Empty;
        string ext       = Path.GetExtension(source);
        bool   isArchive = ArchiveInspector.IsArchive(source);

        // Jeu en dossier (DOS, Windows) : le fichier de profil vit au milieu d'une arborescence installée
        if (!isArchive && sourceDir.Length > 0)
        {
            var dirFiles = req.DirectoryFilesLookup(sourceDir);
            if (dirFiles.Count > 1)
            {
                string profileRel = Path.GetRelativePath(sourceDir, source).Replace('\\', '/');
                long size = dirFiles.Sum(d => d.Size);
                return new RebaseFilePlan
                {
                    SourcePath         = source,
                    Label              = file.Label,
                    Kind               = FileTransferKind.CopyTree,
                    DestRelativeDir    = stem,
                    LaunchRelativePath = $"{stem}/{profileRel}",
                    SourceDirectory    = sourceDir,
                    CopySize           = size,
                    ExtractedSize      = size,
                };
            }
        }

        long copySize = req.FileSizeLookup(source);

        bool extractWanted = isArchive
            && !keepFileName
            && (req.ArchiveMode == ArchiveMode.ExtractAll
                || (req.ArchiveMode == ArchiveMode.ExtractRequired && extractRequired));

        var archive = extractWanted ? req.ArchiveLookup(source) : null;
        if (archive is { IsReadable: true } && archive.Entries.Count > 0)
        {
            var main = MainEntry(archive);
            bool needsCue = NeedsCueSheet(archive, main);
            bool flat = archive.Entries.Count == 1 && req.Layout == ExtractLayout.Auto;
            if (flat)
            {
                string target = stem + Path.GetExtension(main.FileName);
                return new RebaseFilePlan
                {
                    SourcePath            = source,
                    Label                 = file.Label,
                    Kind                  = FileTransferKind.Extract,
                    DestRelativeDir       = string.Empty,
                    LaunchRelativePath    = needsCue ? stem + ".cue" : target,
                    SingleEntryTargetName = target,
                    CueRelativePath       = needsCue ? stem + ".cue" : null,
                    CueBinRelativePath    = needsCue ? target : null,
                    Archive               = archive,
                    CopySize              = copySize,
                    ExtractedSize         = archive.UncompressedSize,
                };
            }

            string mainRel = $"{stem}/{main.InternalPath.Replace('\\', '/')}";
            string? cueRel = needsCue ? WithoutExtension(mainRel) + ".cue" : null;
            return new RebaseFilePlan
            {
                SourcePath         = source,
                Label              = file.Label,
                Kind               = FileTransferKind.Extract,
                DestRelativeDir    = stem,
                LaunchRelativePath = cueRel ?? mainRel,
                CueRelativePath    = cueRel,
                CueBinRelativePath = needsCue ? mainRel : null,
                Archive            = archive,
                CopySize           = copySize,
                ExtractedSize      = archive.UncompressedSize,
            };
        }

        // Copie simple, nom d'origine conservé pour les romsets
        string fileName = keepFileName ? Path.GetFileName(source) : stem + ext;
        return new RebaseFilePlan
        {
            SourcePath         = source,
            Label              = file.Label,
            Kind               = FileTransferKind.Copy,
            LaunchRelativePath = fileName,
            CopySize           = copySize,
            ExtractedSize      = copySize,
        };
    }

    /// <summary>Entrée lançable d'une archive : par priorité d'extension, puis la première par ordre alphabétique.</summary>
    internal static ArchiveEntryInfo MainEntry(ArchiveInfo archive)
    {
        foreach (var ext in MainEntryPriority)
        {
            var hit = archive.Entries
                .Where(e => string.Equals(Path.GetExtension(e.FileName), ext, StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.InternalPath, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (hit is not null) return hit;
        }
        return archive.Entries.OrderBy(e => e.InternalPath, StringComparer.OrdinalIgnoreCase).First();
    }

    /// <summary>
    /// True si l'archive ne livre qu'une image disque brute (.bin ou .img) sans aucun descripteur : aucun émulateur CD
    /// ne la liste telle quelle, un .cue à piste unique sera écrit à côté. Le seuil de taille écarte les ROMs de
    /// cartouche qui portent aussi l'extension .bin (Megadrive, 32X, Atari) : la plus grosse fait quelques mégaoctets.
    /// </summary>
    internal static bool NeedsCueSheet(ArchiveInfo archive, ArchiveEntryInfo main)
        => IsRawDiscImage(main.FileName)
           && main.Length >= DiscImageMinBytes
           && archive.Entries.Count(e => IsRawDiscImage(e.FileName)) == 1;

    private static bool IsRawDiscImage(string fileName)
    {
        string ext = Path.GetExtension(fileName);
        return string.Equals(ext, ".bin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".img", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Chemin relatif sans l'extension de son dernier segment (un point dans un nom de dossier n'est pas une extension).</summary>
    internal static string WithoutExtension(string relativePath)
    {
        int slash = relativePath.LastIndexOf('/');
        string dir  = slash >= 0 ? relativePath[..(slash + 1)] : string.Empty;
        string name = slash >= 0 ? relativePath[(slash + 1)..] : relativePath;
        return dir + Path.GetFileNameWithoutExtension(name);
    }

    /// <summary>
    /// Nom (sans extension) qui sert à la jaquette : celui du fichier lançable quand il est à plat
    /// (convention d'art local d'EmulationStation), celui du dossier sinon.
    /// </summary>
    private static string CoverStem(RebaseFilePlan file, string stem)
        => file.DestRelativeDir.Length == 0
            ? Path.GetFileNameWithoutExtension(file.LaunchRelativePath)
            : stem;

    // ── Helpers de nommage ────────────────────────────────────────────────

    /// <summary>Numéro de disque lu dans un libellé Derby ("(Disc 2)", "(Disque 2)", "(CD 2)"), null s'il n'y en a pas.</summary>
    internal static int? DiscNumber(string label)
    {
        var m = DiscMarker.Match(label);
        return m.Success && int.TryParse(m.Groups[1].Value, out int n) ? n : null;
    }

    /// <summary>Libellé sans son marqueur de disque.</summary>
    internal static string StripDiscMarker(string label)
        => DiscMarker.Replace(label, string.Empty).Trim();

    /// <summary>
    /// Remplace les caractères interdits Windows par "-", fusionne les tirets multiples, retire les tirets,
    /// espaces et points en bordure. Même règle que la 1.2.0 : les jeux à fichier unique gardent leur nom.
    /// </summary>
    public static string SanitizeName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '-');
        while (name.Contains("--"))
            name = name.Replace("--", "-");
        return name.Trim('-').Trim().TrimEnd('.', ' ');
    }
}
