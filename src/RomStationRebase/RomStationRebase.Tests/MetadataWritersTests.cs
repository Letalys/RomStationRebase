using System.IO;
using System.Xml.Linq;
using RomStationRebase.Models;
using RomStationRebase.Services;
using Xunit;

namespace RomStationRebase.Tests;

/// <summary>Formats de métadonnées par architecture et .cue des images disque livrées seules, sur de vrais fichiers temporaires.</summary>
public sealed class MetadataWritersTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rsr-meta-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTime Now = new(2026, 9, 19, 21, 30, 0);

    public MetadataWritersTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* fichier temporaire : sans conséquence */ }
    }

    private static GamelistGame Game(string path, string name, string? image = null, string? desc = null,
        int? year = null, string? publisher = null, long? size = null)
        => new(path, name, image, desc, year, "Dev", publisher, "Action", "1-2", size);

    // ── Registre des formats ──────────────────────────────────────────────

    [Theory]
    [InlineData("emulationstation", "psx", "psx/gamelist.xml")]
    [InlineData("esde",             "psx", "ES-DE/gamelists/psx/gamelist.xml")]
    [InlineData("miyoo",            "PS",  "PS/miyoogamelist.xml")]
    [InlineData("pegasus",          "psx", "psx/metadata.pegasus.txt")]
    [InlineData("logiqx",           "psx", "psx/psx.dat")]
    [InlineData("LOGIQX",           "roms/psx", "roms/psx/psx.dat")]
    public void Each_format_decides_its_file_name_and_location(string format, string folder, string expected)
        => Assert.Equal(expected, MetadataFormats.RelativePath(format, folder));

    [Fact]
    public void Only_formats_that_carry_a_cover_write_an_image()
    {
        Assert.True(MetadataFormats.WritesImage("emulationstation"));
        Assert.True(MetadataFormats.WritesImage("miyoo"));
        Assert.False(MetadataFormats.WritesImage("esde"));
        Assert.False(MetadataFormats.WritesImage("logiqx"));
        Assert.False(MetadataFormats.WritesImage(null));
        Assert.Null(MetadataFormats.Normalize("inconnu"));
    }

    // ── ES-DE et Miyoo : dialectes du gamelist EmulationStation ───────────

    [Fact]
    public void EsDe_gamelist_has_no_image_tag_and_still_merges()
    {
        string path = Path.Combine(_dir, "ES-DE", "gamelists", "psx", "gamelist.xml");
        MetadataWriterService.Write("esde", path, "psx", [Game("./A.cue", "A", image: "./images/A.png")], Now, false);

        // Le joueur marque un favori, puis un second rebase ajoute un jeu
        var doc = XDocument.Load(path);
        doc.Root!.Element("game")!.Add(new XElement("favorite", "true"));
        doc.Save(path);
        var result = MetadataWriterService.Write("esde", path, "psx", [Game("./B.cue", "B")], Now, false);

        doc = XDocument.Load(path);
        Assert.True(result.Merged);
        Assert.Equal(2, doc.Root!.Elements("game").Count());
        Assert.Empty(doc.Descendants("image"));
        Assert.Empty(doc.Descendants("provider"));
        Assert.Equal("true", doc.Root.Elements("game").First().Element("favorite")!.Value);
    }

    [Fact]
    public void Miyoo_gamelist_keeps_only_path_name_image_and_skips_subfolder_games()
    {
        string path = Path.Combine(_dir, "PS", "miyoogamelist.xml");
        MetadataWriterService.Write("miyoo", path, "PS",
        [
            Game("./Kirby.zip", "Kirby", image: "./Imgs/Kirby.png", desc: "Une description", year: 1993),
            Game("./Wipeout/w.cue", "Wipeout", image: "./Imgs/Wipeout.png"),
        ], Now, false);

        var doc  = XDocument.Load(path);
        var game = Assert.Single(doc.Root!.Elements("game"));
        Assert.Equal(["path", "name", "image"], game.Elements().Select(e => e.Name.LocalName));
        Assert.Equal("./Imgs/Kirby.png", game.Element("image")!.Value);
    }

    // ── Pegasus ───────────────────────────────────────────────────────────

    [Fact]
    public void Pegasus_file_lists_games_in_a_collection_and_breaks_paragraphs_with_a_dot()
    {
        string path = Path.Combine(_dir, "psx", "metadata.pegasus.txt");
        MetadataWriterService.Write("pegasus", path, "psx",
            [Game("./Kirby.zip", "Kirby : Dream", image: "./images/Kirby-image.png", desc: "Ligne une\n\nLigne deux", year: 1993, publisher: "Nintendo")],
            Now, false);

        string[] lines = File.ReadAllLines(path);
        Assert.Equal("collection: psx", lines[0]);
        Assert.Contains(PegasusMetadataWriter.SourceMarker, lines);
        Assert.Contains("  Kirby.zip", lines);
        Assert.Contains("game: Kirby : Dream", lines);
        Assert.Contains("file: Kirby.zip", lines);
        Assert.Contains("release: 1993", lines);
        Assert.Contains("assets.boxFront: images/Kirby-image.png", lines);
        int d = Array.IndexOf(lines, "description: Ligne une");
        Assert.Equal("  .", lines[d + 1]);
        Assert.Equal("  Ligne deux", lines[d + 2]);
    }

    [Fact]
    public void Pegasus_merge_keeps_foreign_entries_and_replaces_a_known_game_in_place()
    {
        string path = Path.Combine(_dir, "metadata.pegasus.txt");
        File.WriteAllLines(path,
        [
            "# mes réglages",
            "collection: PlayStation",
            "extension: cue",
            "launch: monemulateur {file.path}",
            "",
            "game: Ancien titre",
            "file: A.cue",
            "x-note: à moi",
            "",
            "game: Jeu étranger",
            "files:",
            "  Z1.cue",
            "  Z2.cue",
        ]);

        var result = MetadataWriterService.Write("pegasus", path, "psx",
            [Game("./A.cue", "Nouveau titre"), Game("./B.cue", "B")], Now, backupExisting: true);

        string text = File.ReadAllText(path);
        Assert.True(result.Merged);
        Assert.NotNull(result.UserBackupPath);
        Assert.Contains("launch: monemulateur {file.path}", text);   // la collection de l'utilisateur est intacte
        Assert.Contains("game: Jeu étranger", text);
        Assert.Contains("game: Nouveau titre", text);
        Assert.DoesNotContain("Ancien titre", text);
        Assert.True(text.IndexOf("game: Nouveau titre", StringComparison.Ordinal)
                  < text.IndexOf("game: Jeu étranger", StringComparison.Ordinal)); // remplacé sur place
        foreach (string f in new[] { "  A.cue", "  B.cue", "  Z1.cue", "  Z2.cue" })
            Assert.Contains(f, File.ReadAllLines(path));

        // Un troisième passage ne duplique ni la collection de RSR ni les jeux
        MetadataWriterService.Write("pegasus", path, "psx", [Game("./B.cue", "B")], Now, false);
        string[] lines = File.ReadAllLines(path);
        Assert.Single(lines, l => l == PegasusMetadataWriter.SourceMarker);
        Assert.Single(lines, l => l == "game: B");
    }

    // ── Logiqx ────────────────────────────────────────────────────────────

    [Fact]
    public void Logiqx_dat_is_valid_and_merges_by_rom_name()
    {
        string path = Path.Combine(_dir, "psx", "psx.dat");
        MetadataWriterService.Write("logiqx", path, "psx",
            [Game("./Tarzan.zip", "Disney's Tarzan", desc: "Un jeu", year: 1999, publisher: "Sony", size: 334104111)], Now, false);
        var result = MetadataWriterService.Write("logiqx", path, "psx",
            [Game("./Tarzan.zip", "Tarzan", size: 42), Game("./Spyro.zip", "Spyro", year: 1998)], Now, false);

        string raw = File.ReadAllText(path);
        Assert.Contains("<!DOCTYPE datafile PUBLIC \"-//Logiqx//DTD ROM Management Datafile//EN\"", raw);

        var settings = new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Ignore, XmlResolver = null };
        using var reader = System.Xml.XmlReader.Create(path, settings);
        var doc = XDocument.Load(reader);

        Assert.True(result.Merged);
        Assert.Equal("psx", doc.Root!.Element("header")!.Element("name")!.Value);
        var games = doc.Root.Elements("game").ToList();
        Assert.Equal(2, games.Count);
        Assert.Equal("Tarzan", games[0].Attribute("name")!.Value);
        Assert.Equal("Tarzan.zip", games[0].Element("rom")!.Attribute("name")!.Value);
        Assert.Equal("42", games[0].Element("rom")!.Attribute("size")!.Value);
        // Ordre imposé par la DTD, y compris pour un élément ajouté lors d'une fusion
        Assert.Equal(["description", "year", "manufacturer", "rom"], games[0].Elements().Select(e => e.Name.LocalName));
    }

    // ── .cue d'une image disque livrée seule ──────────────────────────────

    [Theory]
    [InlineData((byte)2, "MODE2/2352")]
    [InlineData((byte)1, "MODE1/2352")]
    public void Cue_track_mode_is_read_from_the_first_sector(byte mode, string expected)
    {
        string bin = Path.Combine(_dir, "Ronin Blade.bin");
        byte[] sector = new byte[2352];
        sector[0] = 0x00; sector[11] = 0x00;
        for (int i = 1; i <= 10; i++) sector[i] = 0xFF;
        sector[15] = mode;
        File.WriteAllBytes(bin, sector);

        string cue = Path.Combine(_dir, "Ronin Blade.cue");
        CueSheetService.WriteFor(bin, cue);

        Assert.Equal(
            $"FILE \"Ronin Blade.bin\" BINARY\r\n  TRACK 01 {expected}\r\n    INDEX 01 00:00:00\r\n",
            File.ReadAllText(cue));
    }

    [Fact]
    public void Image_without_sector_header_is_described_as_cooked_mode1()
    {
        string bin = Path.Combine(_dir, "iso.bin");
        File.WriteAllBytes(bin, new byte[4096]);
        Assert.Equal("MODE1/2048", CueSheetService.DetectTrackMode(bin));
    }
}
