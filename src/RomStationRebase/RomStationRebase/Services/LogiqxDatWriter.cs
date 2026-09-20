using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using RomStationRebase.Models;

namespace RomStationRebase.Services;

/// <summary>
/// Écrit ou fusionne un fichier .dat au format Logiqx (celui que produit Skraper, qu'importe Daijisho
/// et que lisent les gestionnaires de ROMs). Un jeu est reconnu par le nom de son fichier ; les jeux
/// qu'un rebase précédent ou un autre outil y a inscrits sont conservés.
/// </summary>
public static class LogiqxDatWriter
{
    private const string RootName = "datafile";

    public static GamelistWriteResult WriteOrMerge(string filePath, string systemName,
        IReadOnlyList<GamelistGame> games, DateTime now, bool backupExisting)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(games);

        string? parent = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(parent))
            Directory.CreateDirectory(parent);

        XDocument? existing = TryLoad(filePath);
        bool merged = existing is not null;

        string? backupPath = null, userBackup = null;
        if (!merged && File.Exists(filePath))
            backupPath = GamelistService.BackupUnreadableFile(filePath, now);
        else if (merged && backupExisting)
            userBackup = GamelistService.CopyBackup(filePath, now);

        XDocument doc  = existing ?? CreateDocument(systemName, now);
        XElement  root = doc.Root!;

        // La DTD est ignorée à la lecture (jamais de réseau) : la déclaration est reposée pour que le fichier reste un Logiqx valide
        if (doc.DocumentType is null)
            root.AddBeforeSelf(new XDocumentType(RootName, "-//Logiqx//DTD ROM Management Datafile//EN",
                "http://www.logiqx.com/Dats/datafile.dtd", null));

        // Index des jeux présents, par nom de fichier de leur première ROM
        var index = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
        foreach (XElement game in root.Elements("game"))
        {
            string? rom = game.Element("rom")?.Attribute("name")?.Value;
            if (!string.IsNullOrWhiteSpace(rom))
                index.TryAdd(GamelistService.NormalizePath(rom), game);
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (GamelistGame data in games)
        {
            string key = GamelistService.NormalizePath(data.Path);
            seen.Add(key);
            if (!index.TryGetValue(key, out XElement? game))
            {
                game = new XElement("game");
                root.Add(game);
                index[key] = game;
            }
            Apply(game, data, key);
        }

        string temp = filePath + ".tmp";
        var settings = new XmlWriterSettings
        {
            Indent      = true,
            IndentChars = "  ",
            Encoding    = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };
        using (XmlWriter writer = XmlWriter.Create(temp, settings))
            doc.Save(writer);
        File.Move(temp, filePath, overwrite: true);

        return new GamelistWriteResult
        {
            Merged = merged, BackupPath = backupPath, UserBackupPath = userBackup, Written = seen.Count,
        };
    }

    /// <summary>L'ordre des éléments est imposé par la DTD : description, year, manufacturer, rom.</summary>
    private static void Apply(XElement game, GamelistGame data, string romName)
    {
        game.SetAttributeValue("name", data.Name);

        SetChild(game, "description", string.IsNullOrWhiteSpace(data.Description) ? data.Name : data.Description);
        SetChild(game, "year", data.Year?.ToString("D4", CultureInfo.InvariantCulture));
        SetChild(game, "manufacturer", data.Publisher ?? data.Developer);

        XElement? rom = game.Element("rom");
        if (rom is null)
        {
            rom = new XElement("rom");
            game.Add(rom);
        }
        rom.SetAttributeValue("name", romName);
        rom.SetAttributeValue("size", (data.Size ?? 0).ToString(CultureInfo.InvariantCulture));
    }

    private static void SetChild(XElement game, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        XElement? child = game.Element(name);
        if (child is not null) { child.Value = value; return; }

        // Insertion avant le premier élément qui doit suivre, pour respecter la DTD
        string[] order = ["description", "year", "manufacturer", "rom"];
        int rank = Array.IndexOf(order, name);
        XElement? next = game.Elements().FirstOrDefault(e => Array.IndexOf(order, e.Name.LocalName) > rank);
        if (next is null) game.Add(new XElement(name, value));
        else next.AddBeforeSelf(new XElement(name, value));
    }

    private static XDocument CreateDocument(string systemName, DateTime now)
        => new(
            new XDeclaration("1.0", "utf-8", null),
            new XDocumentType(RootName, "-//Logiqx//DTD ROM Management Datafile//EN",
                "http://www.logiqx.com/Dats/datafile.dtd", null),
            new XElement(RootName,
                new XElement("header",
                    new XElement("name", systemName),
                    new XElement("description", $"{systemName} - RomStation"),
                    new XElement("version", "1.0"),
                    new XElement("date", now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                    new XElement("author", "RomStation Rebase"))));

    /// <summary>Charge un .dat existant sans jamais aller chercher sa DTD sur le réseau. Null s'il est absent ou illisible.</summary>
    private static XDocument? TryLoad(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
            using XmlReader reader = XmlReader.Create(filePath, settings);
            XDocument doc = XDocument.Load(reader);
            return doc.Root is not null
                && string.Equals(doc.Root.Name.LocalName, RootName, StringComparison.OrdinalIgnoreCase)
                ? doc : null;
        }
        catch (XmlException)
        {
            return null;
        }
    }
}
