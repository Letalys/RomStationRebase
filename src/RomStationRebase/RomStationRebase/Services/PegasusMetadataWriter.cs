using System.IO;
using System.Text;
using RomStationRebase.Models;

namespace RomStationRebase.Services;

/// <summary>
/// Écrit ou fusionne un metadata.pegasus.txt (Pegasus Frontend) dans un dossier système.
/// Format texte « clé: valeur » : une entrée commence par "collection:" ou "game:" en début de ligne,
/// une valeur se poursuit sur les lignes indentées, un point seul marque un saut de paragraphe.
/// Fusion : les entrées que RSR ne connaît pas sont recopiées telles quelles, un jeu est reconnu par son premier fichier.
/// La collection de RSR (marquée x-source) liste tous les fichiers des jeux ; une collection de l'utilisateur n'est jamais touchée.
/// </summary>
public static class PegasusMetadataWriter
{
    internal const string SourceMarker = "x-source: RomStation Rebase";

    public static GamelistWriteResult WriteOrMerge(string filePath, string collectionName,
        IReadOnlyList<GamelistGame> games, DateTime now, bool backupExisting)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(games);

        string? parent = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(parent))
            Directory.CreateDirectory(parent);

        bool    merged     = File.Exists(filePath);
        string? userBackup = merged && backupExisting ? GamelistService.CopyBackup(filePath, now) : null;
        var     blocks     = merged ? ParseBlocks(File.ReadAllLines(filePath)) : [];

        // Le dernier jeu fourni pour un même fichier gagne, à la position du premier
        var byFile = new Dictionary<string, GamelistGame>(StringComparer.OrdinalIgnoreCase);
        var order  = new List<string>();
        foreach (var game in games)
        {
            string key = GamelistService.NormalizePath(game.Path);
            if (!byFile.ContainsKey(key)) order.Add(key);
            byFile[key] = game;
        }

        // Jeux déjà présents : remplacés sur place, les autres blocs restent intacts
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < blocks.Count; i++)
        {
            if (blocks[i].Kind != BlockKind.Game) continue;
            string? first = blocks[i].Files.FirstOrDefault();
            if (first is not null && byFile.TryGetValue(first, out var game) && written.Add(first))
                blocks[i] = GameBlock(game);
        }
        foreach (string key in order)
            if (written.Add(key))
                blocks.Add(GameBlock(byFile[key]));

        // Collection de RSR : reconstruite d'après tous les jeux du fichier, placée en tête si elle est nouvelle
        var allFiles = blocks.Where(b => b.Kind == BlockKind.Game).SelectMany(b => b.Files)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var collection = CollectionBlock(collectionName, allFiles);
        int ours = blocks.FindIndex(b => b.Kind == BlockKind.Collection && b.IsOurs);
        if (ours >= 0) blocks[ours] = collection;
        else blocks.Insert(blocks.Count > 0 && blocks[0].Kind == BlockKind.Preamble ? 1 : 0, collection);

        var sb = new StringBuilder();
        foreach (var block in blocks)
        {
            foreach (string line in block.Lines) sb.Append(line).Append('\n');
            if (block.Lines.Count > 0 && block.Lines[^1].Length > 0) sb.Append('\n');
        }

        string temp = filePath + ".tmp";
        File.WriteAllText(temp, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temp, filePath, overwrite: true);

        return new GamelistWriteResult { Merged = merged, UserBackupPath = userBackup, Written = byFile.Count };
    }

    // ── Lecture ───────────────────────────────────────────────────────────

    internal enum BlockKind { Preamble, Collection, Game }

    internal sealed class Block
    {
        public BlockKind    Kind  { get; init; }
        public List<string> Lines { get; init; } = [];
        /// <summary>Fichiers du jeu, normalisés (sans "./", séparateur "/").</summary>
        public List<string> Files { get; init; } = [];
        public bool         IsOurs { get; init; }
    }

    /// <summary>Découpe le fichier en blocs. Ce qui précède la première entrée (commentaires) forme un préambule conservé.</summary>
    internal static List<Block> ParseBlocks(IReadOnlyList<string> lines)
    {
        var blocks  = new List<Block>();
        var current = new List<string>();
        var kind    = BlockKind.Preamble;

        void Flush()
        {
            // Lignes vides de fin retirées : l'écriture en remet une entre deux blocs
            while (current.Count > 0 && string.IsNullOrWhiteSpace(current[^1])) current.RemoveAt(current.Count - 1);
            if (current.Count > 0)
                blocks.Add(new Block
                {
                    Kind   = kind,
                    Lines  = current,
                    Files  = kind == BlockKind.Game ? ReadFiles(current) : [],
                    IsOurs = current.Any(l => l.Trim().Equals(SourceMarker, StringComparison.OrdinalIgnoreCase)),
                });
            current = [];
        }

        foreach (string line in lines)
        {
            bool isCollection = line.StartsWith("collection:", StringComparison.OrdinalIgnoreCase);
            bool isGame       = line.StartsWith("game:", StringComparison.OrdinalIgnoreCase);
            if (isCollection || isGame)
            {
                Flush();
                kind = isCollection ? BlockKind.Collection : BlockKind.Game;
            }
            current.Add(line);
        }
        Flush();
        return blocks;
    }

    /// <summary>Valeurs de "file:" ou "files:" d'un bloc de jeu, lignes de continuation comprises.</summary>
    private static List<string> ReadFiles(List<string> lines)
    {
        var files = new List<string>();
        bool inFiles = false;
        foreach (string line in lines)
        {
            bool continuation = line.Length > 0 && (line[0] == ' ' || line[0] == '\t');
            if (continuation)
            {
                if (inFiles && line.Trim().Length > 0) files.Add(GamelistService.NormalizePath(line));
                continue;
            }
            inFiles = line.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                   || line.StartsWith("files:", StringComparison.OrdinalIgnoreCase);
            if (!inFiles) continue;
            string value = line[(line.IndexOf(':') + 1)..].Trim();
            if (value.Length > 0) files.Add(GamelistService.NormalizePath(value));
        }
        return files;
    }

    // ── Ecriture ──────────────────────────────────────────────────────────

    private static Block CollectionBlock(string name, List<string> files)
    {
        var lines = new List<string> { $"collection: {OneLine(name)}", SourceMarker, "files:" };
        lines.AddRange(files.Select(f => "  " + f));
        return new Block { Kind = BlockKind.Collection, Lines = lines, IsOurs = true };
    }

    private static Block GameBlock(GamelistGame game)
    {
        string file  = GamelistService.NormalizePath(game.Path);
        var    lines = new List<string> { $"game: {OneLine(game.Name)}", $"file: {file}" };

        void Add(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) lines.Add($"{key}: {OneLine(value)}");
        }

        Add("developer", game.Developer);
        Add("publisher", game.Publisher);
        Add("genre",     game.Genre);
        Add("players",   game.Players);
        Add("release",   game.Year is int year ? year.ToString("D4") : null);
        if (!string.IsNullOrWhiteSpace(game.Image))
            lines.Add($"assets.boxFront: {GamelistService.NormalizePath(game.Image)}");
        if (!string.IsNullOrWhiteSpace(game.Description))
            lines.AddRange(MultiLine("description", game.Description));

        return new Block { Kind = BlockKind.Game, Lines = lines, Files = [file] };
    }

    /// <summary>Valeur multiligne : suite indentée de deux espaces, ligne vide rendue par un point seul.</summary>
    internal static List<string> MultiLine(string key, string value)
    {
        var parts = value.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
            .Select(p => p.Trim()).ToList();
        while (parts.Count > 0 && parts[^1].Length == 0) parts.RemoveAt(parts.Count - 1);
        while (parts.Count > 0 && parts[0].Length == 0)  parts.RemoveAt(0);

        var lines = new List<string>();
        for (int i = 0; i < parts.Count; i++)
        {
            string text = parts[i].Length == 0 ? "." : parts[i];
            lines.Add(i == 0 ? $"{key}: {text}" : "  " + text);
        }
        return lines;
    }

    private static string OneLine(string value)
        => value.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
}
