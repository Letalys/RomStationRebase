using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using RomStationRebase.Models;

namespace RomStationRebase.Services;

/// <summary>
/// Écrit ou fusionne un gamelist.xml au format de la famille EmulationStation (RetroPie, ArkOS, Batocera).
/// Un fichier existant valide est fusionné : seules les métadonnées fournies sont mises à jour,
/// tout ce que l'utilisateur ou le frontend y a ajouté (favoris, compteurs, jeux d'un rebase précédent) est conservé.
/// </summary>
public static class GamelistService
{
    private const string RootName     = "gameList";
    private const string GameName     = "game";
    private const string PathName     = "path";
    private const string ProviderName = "provider";

    /// <summary>
    /// Écrit le gamelist.xml en fusionnant avec un fichier existant valide, ou en le recréant
    /// (après sauvegarde) s'il est illisible. Les exceptions d'accès disque ne sont pas interceptées.
    /// </summary>
    public static GamelistWriteResult WriteOrMerge(string gamelistPath, IReadOnlyList<GamelistGame> games, bool backupExisting = false)
        => WriteOrMerge(gamelistPath, games, DateTime.Now, backupExisting);

    /// <summary>
    /// Variante avec horodatage injectable — sert à nommer les sauvegardes.
    /// Avec backupExisting, un fichier existant valide est d'abord copié en gamelist.xml.yyyyMMdd (jamais écrasé).
    /// </summary>
    internal static GamelistWriteResult WriteOrMerge(string gamelistPath, IReadOnlyList<GamelistGame> games, DateTime now, bool backupExisting = false, bool withProvider = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gamelistPath);
        ArgumentNullException.ThrowIfNull(games);

        string? parent = Path.GetDirectoryName(gamelistPath);
        if (!string.IsNullOrEmpty(parent))
            Directory.CreateDirectory(parent);

        // Doublons dans l'entrée : le dernier gagne, mais la position du premier est conservée.
        var byPath = new Dictionary<string, GamelistGame>(StringComparer.OrdinalIgnoreCase);
        var order  = new List<string>();
        foreach (GamelistGame game in games)
        {
            string key = NormalizePath(game.Path);
            if (!byPath.ContainsKey(key))
                order.Add(key);
            byPath[key] = game;
        }

        XElement? existingRoot = TryLoadExistingRoot(gamelistPath);
        bool merged = existingRoot is not null;

        string? backupPath     = null;
        string? userBackupPath = null;
        if (!merged && File.Exists(gamelistPath))
            backupPath = BackupUnreadableFile(gamelistPath, now);
        else if (merged && backupExisting)
            userBackupPath = CopyBackup(gamelistPath, now);

        XElement  root = existingRoot ?? CreateNewRoot(withProvider);
        XDocument doc  = root.Document ?? new XDocument(new XDeclaration("1.0", "utf-8", null), root);

        // Index des jeux déjà présents, par chemin normalisé (le premier l'emporte en cas de doublon dans le fichier).
        var index = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
        foreach (XElement game in root.Elements(GameName))
        {
            string? path = game.Element(PathName)?.Value;
            if (!string.IsNullOrWhiteSpace(path))
                index.TryAdd(NormalizePath(path), game);
        }

        foreach (string key in order)
        {
            GamelistGame game = byPath[key];
            if (index.TryGetValue(key, out XElement? element))
            {
                ApplyMetadata(element, game);
            }
            else
            {
                var created = new XElement(GameName, new XElement(PathName, game.Path));
                ApplyMetadata(created, game);
                root.Add(created);
                index[key] = created;
            }
        }

        WriteAtomically(doc, gamelistPath);

        return new GamelistWriteResult
        {
            Merged         = merged,
            BackupPath     = backupPath,
            UserBackupPath = userBackupPath,
            Written        = byPath.Count,
        };
    }

    /// <summary>
    /// Copie de sauvegarde demandée par l'utilisateur avant fusion : gamelist.xml.yyyyMMdd, puis
    /// gamelist.xml.yyyyMMdd-HHmmss, puis suffixe -2, -3… Une sauvegarde n'est jamais écrasée.
    /// </summary>
    internal static string CopyBackup(string gamelistPath, DateTime now)
    {
        string candidate = $"{gamelistPath}.{now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}";
        if (File.Exists(candidate))
        {
            string baseName = $"{gamelistPath}.{now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}";
            candidate = baseName;
            for (int suffix = 2; File.Exists(candidate); suffix++)
                candidate = $"{baseName}-{suffix}";
        }
        File.Copy(gamelistPath, candidate);
        return candidate;
    }

    /// <summary>
    /// Normalise un chemin de gamelist pour la comparaison : trim, séparateurs Unix, préfixe "./" retiré.
    /// La casse est ignorée par le comparateur du dictionnaire appelant.
    /// </summary>
    internal static string NormalizePath(string path)
    {
        string normalized = path.Trim().Replace('\\', '/');
        if (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];
        return normalized;
    }

    /// <summary>
    /// Charge la racine d'un fichier existant, ou null si le fichier est absent, vide, mal formé
    /// ou n'a pas la racine attendue. Les erreurs d'accès disque remontent à l'appelant.
    /// </summary>
    private static XElement? TryLoadExistingRoot(string gamelistPath)
    {
        if (!File.Exists(gamelistPath))
            return null;

        XDocument doc;
        try
        {
            doc = XDocument.Load(gamelistPath, LoadOptions.None);
        }
        catch (XmlException)
        {
            return null;
        }

        XElement? root = doc.Root;
        return root is not null && string.Equals(root.Name.LocalName, RootName, StringComparison.OrdinalIgnoreCase)
            ? root
            : null;
    }

    /// <summary>Racine neuve avec le bloc provider identifiant RomStation Rebase comme source.</summary>
    private static XElement CreateNewRoot(bool withProvider)
        => withProvider
            ? new(RootName,
                new XElement(ProviderName,
                    new XElement("software", "RomStation Rebase"),
                    new XElement("database", "RomStation")))
            : new(RootName);

    /// <summary>
    /// Met à jour ou crée uniquement les éléments gérés par RSR, sans toucher aux autres enfants
    /// (favorite, playcount, lastplayed, rating…) ni aux attributs du jeu. Un champ null est ignoré.
    /// </summary>
    private static void ApplyMetadata(XElement game, GamelistGame data)
    {
        SetIfProvided(game, "name",        data.Name);
        SetIfProvided(game, "desc",        data.Description);
        SetIfProvided(game, "image",       data.Image);
        SetIfProvided(game, "releasedate", data.Year is int year ? $"{year:D4}0101T000000" : null);
        SetIfProvided(game, "developer",   data.Developer);
        SetIfProvided(game, "publisher",   data.Publisher);
        SetIfProvided(game, "genre",       data.Genre);
        SetIfProvided(game, "players",     data.Players);
    }

    private static void SetIfProvided(XElement game, string elementName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        XElement? element = game.Element(elementName);
        if (element is null)
            game.Add(new XElement(elementName, value));
        else
            element.Value = value;
    }

    /// <summary>
    /// Renomme un fichier illisible en gamelist.xml.bak-yyyyMMdd-HHmmss, avec suffixe -2, -3…
    /// si une sauvegarde porte déjà ce nom. Une sauvegarde n'est jamais écrasée.
    /// </summary>
    internal static string BackupUnreadableFile(string gamelistPath, DateTime now)
    {
        string stamp     = now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string baseName  = $"{gamelistPath}.bak-{stamp}";
        string candidate = baseName;

        for (int suffix = 2; File.Exists(candidate); suffix++)
            candidate = $"{baseName}-{suffix}";

        File.Move(gamelistPath, candidate);
        return candidate;
    }

    /// <summary>
    /// Écrit dans un fichier temporaire voisin puis le substitue au fichier cible : un retrait du support
    /// en cours d'écriture ne laisse jamais un gamelist tronqué. UTF-8 sans BOM, indenté.
    /// </summary>
    private static void WriteAtomically(XDocument doc, string gamelistPath)
    {
        string tempPath = gamelistPath + ".tmp";
        var settings = new XmlWriterSettings
        {
            Indent             = true,
            IndentChars        = "  ",
            Encoding           = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            OmitXmlDeclaration = false,
        };

        using (XmlWriter writer = XmlWriter.Create(tempPath, settings))
        {
            doc.Save(writer);
        }

        File.Move(tempPath, gamelistPath, overwrite: true);
    }
}
