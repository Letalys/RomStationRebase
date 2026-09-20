using System.IO;
using System.Text;
using System.Xml.Linq;
using RomStationRebase.Models;
using RomStationRebase.Services;
using Xunit;

namespace RomStationRebase.Tests;

/// <summary>Fige le format et les règles de fusion du gamelist.xml EmulationStation produit par la 1.3.0.</summary>
public sealed class GamelistServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rsr-tests", Guid.NewGuid().ToString("N"));

    private string GamelistPath => Path.Combine(_dir, "gamelist.xml");

    public GamelistServiceTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static GamelistGame Game(
        string path, string name, string? desc = null, string? image = null, int? year = null,
        string? developer = null, string? publisher = null, string? genre = null, string? players = null)
        => new(path, name, image, desc, year, developer, publisher, genre, players);

    private XElement Root()
        => XDocument.Load(GamelistPath).Root ?? throw new InvalidOperationException("Racine absente");

    private static List<XElement> Games(XElement root) => root.Elements("game").ToList();

    private static string? Val(XElement game, string name) => game.Element(name)?.Value;

    private static XElement GameAt(XElement root, string path)
        => Games(root).FirstOrDefault(g => Val(g, "path") == path)
           ?? throw new InvalidOperationException($"Jeu absent : {path}");

    private void WriteExisting(string xml) => File.WriteAllText(GamelistPath, xml, new UTF8Encoding(false));

    // ── Création ──────────────────────────────────────────────────────────

    [Fact]
    public void New_file_contains_provider_and_every_field_correctly_escaped()
    {
        var result = GamelistService.WriteOrMerge(GamelistPath,
        [
            Game("./Command & Conquer.zip", "Command & Conquer",
                 desc: "Un jeu de stratégie « très » célèbre — à l'époque <du> Pentium.",
                 image: "./images/Command & Conquer-image.png", year: 2003,
                 developer: "Westwood", publisher: "Virgin", genre: "Stratégie, Action", players: "1-2"),
        ]);

        Assert.False(result.Merged);
        Assert.Null(result.BackupPath);
        Assert.Equal(1, result.Written);

        XElement root = Root();
        Assert.Equal("gameList", root.Name.LocalName);
        Assert.Equal("RomStation Rebase", root.Element("provider")?.Element("software")?.Value);
        Assert.Equal("RomStation", root.Element("provider")?.Element("database")?.Value);

        XElement game = Assert.Single(Games(root));
        Assert.Equal("./Command & Conquer.zip", Val(game, "path"));
        Assert.Equal("Command & Conquer", Val(game, "name"));
        Assert.Equal("Un jeu de stratégie « très » célèbre — à l'époque <du> Pentium.", Val(game, "desc"));
        Assert.Equal("./images/Command & Conquer-image.png", Val(game, "image"));
        Assert.Equal("20030101T000000", Val(game, "releasedate"));
        Assert.Equal("Westwood", Val(game, "developer"));
        Assert.Equal("Virgin", Val(game, "publisher"));
        Assert.Equal("Stratégie, Action", Val(game, "genre"));
        Assert.Equal("1-2", Val(game, "players"));

        // Forme physique : déclaration XML, UTF-8 sans BOM, entités échappées, indentation, pas de résidu temporaire.
        byte[] bytes = File.ReadAllBytes(GamelistPath);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "BOM inattendu");
        string text = Encoding.UTF8.GetString(bytes);
        Assert.StartsWith("<?xml version=\"1.0\"", text);
        Assert.Contains("Command &amp; Conquer", text);
        Assert.Contains("&lt;du&gt;", text);
        Assert.Contains("\n  <game>", text);
        Assert.False(File.Exists(GamelistPath + ".tmp"));
    }

    [Fact]
    public void Null_or_empty_fields_are_not_written()
    {
        GamelistService.WriteOrMerge(GamelistPath, [Game("./Kirby.zip", "Kirby", genre: "", players: "  ")]);

        XElement game = Assert.Single(Games(Root()));
        Assert.Equal(["path", "name"], game.Elements().Select(e => e.Name.LocalName));
    }

    [Fact]
    public void Parent_directory_is_created_when_missing()
    {
        string nested = Path.Combine(_dir, "gba", "sub", "gamelist.xml");

        var result = GamelistService.WriteOrMerge(nested, [Game("./Kirby.zip", "Kirby")]);

        Assert.True(File.Exists(nested));
        Assert.False(result.Merged);
    }

    // ── Fusion ────────────────────────────────────────────────────────────

    [Fact]
    public void Merge_updates_managed_fields_and_preserves_everything_else()
    {
        WriteExisting("""
            <?xml version="1.0"?>
            <gameList>
              <folder>
                <path>./hacks</path>
                <name>Hacks</name>
              </folder>
              <game id="1234" source="ScreenScraper">
                <path>./Kirby.zip</path>
                <name>Old name</name>
                <desc>Old description</desc>
                <favorite>true</favorite>
                <hidden>false</hidden>
                <kidgame>true</kidgame>
                <playcount>7</playcount>
                <lastplayed>20250101T120000</lastplayed>
                <rating>0.8</rating>
                <marquee>./images/Kirby-marquee.png</marquee>
              </game>
              <game>
                <path>./Zelda.zip</path>
                <name>Zelda</name>
                <favorite>true</favorite>
              </game>
            </gameList>
            """);

        var result = GamelistService.WriteOrMerge(GamelistPath,
            [Game("./Kirby.zip", "Kirby : Nightmare in Dream Land", desc: "New description", year: 2002)]);

        Assert.True(result.Merged);
        Assert.Null(result.BackupPath);
        Assert.Equal(1, result.Written);

        XElement root = Root();
        Assert.Null(root.Element("provider"));
        Assert.Equal("Hacks", root.Element("folder")?.Element("name")?.Value);
        Assert.Equal(2, Games(root).Count);

        XElement kirby = GameAt(root, "./Kirby.zip");
        Assert.Equal("1234", kirby.Attribute("id")?.Value);
        Assert.Equal("ScreenScraper", kirby.Attribute("source")?.Value);
        Assert.Equal("Kirby : Nightmare in Dream Land", Val(kirby, "name"));
        Assert.Equal("New description", Val(kirby, "desc"));
        Assert.Equal("20020101T000000", Val(kirby, "releasedate"));
        Assert.Equal("true", Val(kirby, "favorite"));
        Assert.Equal("false", Val(kirby, "hidden"));
        Assert.Equal("true", Val(kirby, "kidgame"));
        Assert.Equal("7", Val(kirby, "playcount"));
        Assert.Equal("20250101T120000", Val(kirby, "lastplayed"));
        Assert.Equal("0.8", Val(kirby, "rating"));
        Assert.Equal("./images/Kirby-marquee.png", Val(kirby, "marquee"));

        XElement zelda = GameAt(root, "./Zelda.zip");
        Assert.Equal("Zelda", Val(zelda, "name"));
        Assert.Equal("true", Val(zelda, "favorite"));
    }

    [Fact]
    public void Null_field_does_not_erase_the_existing_value()
    {
        WriteExisting("""
            <gameList>
              <game>
                <path>./Kirby.zip</path>
                <name>Kirby</name>
                <desc>Existing description</desc>
                <image>./images/Kirby-image.png</image>
                <releasedate>20020101T000000</releasedate>
                <developer>HAL</developer>
              </game>
            </gameList>
            """);

        GamelistService.WriteOrMerge(GamelistPath, [Game("./Kirby.zip", "Kirby", publisher: "Nintendo")]);

        XElement game = Assert.Single(Games(Root()));
        Assert.Equal("Existing description", Val(game, "desc"));
        Assert.Equal("./images/Kirby-image.png", Val(game, "image"));
        Assert.Equal("20020101T000000", Val(game, "releasedate"));
        Assert.Equal("HAL", Val(game, "developer"));
        Assert.Equal("Nintendo", Val(game, "publisher"));
    }

    [Theory]
    [InlineData("Kirby.zip",      "./Kirby.zip")]
    [InlineData("./Kirby.zip",    "Kirby.zip")]
    [InlineData(".\\Kirby.zip",   "./Kirby.zip")]
    [InlineData("./Kirby.zip",    ".\\kirby.zip")]
    [InlineData("./KIRBY.ZIP",    "./Kirby.zip")]
    [InlineData(" ./Kirby.zip ",  "./Kirby.zip")]
    [InlineData("./sub\\Kirby.zip", "./sub/Kirby.zip")]
    public void Path_matching_ignores_dot_slash_prefix_separators_and_case(string existingPath, string newPath)
    {
        WriteExisting($"""
            <gameList>
              <game>
                <path>{existingPath}</path>
                <name>Old</name>
                <favorite>true</favorite>
              </game>
            </gameList>
            """);

        GamelistService.WriteOrMerge(GamelistPath, [Game(newPath, "New")]);

        XElement game = Assert.Single(Games(Root()));
        Assert.Equal(existingPath, Val(game, "path"));
        Assert.Equal("New", Val(game, "name"));
        Assert.Equal("true", Val(game, "favorite"));
    }

    [Fact]
    public void Existing_order_is_kept_and_new_games_are_appended()
    {
        WriteExisting("""
            <gameList>
              <provider><software>Skraper</software></provider>
              <game><path>./B.zip</path><name>B</name></game>
              <game><path>./A.zip</path><name>A</name></game>
            </gameList>
            """);

        var result = GamelistService.WriteOrMerge(GamelistPath,
        [
            Game("./C.zip", "C"),
            Game("./A.zip", "A updated"),
            Game("./D.zip", "D"),
        ]);

        Assert.Equal(3, result.Written);
        XElement root = Root();
        Assert.Equal(["./B.zip", "./A.zip", "./C.zip", "./D.zip"], Games(root).Select(g => Val(g, "path")));
        Assert.Equal("A updated", Val(GameAt(root, "./A.zip"), "name"));
        Assert.Equal("Skraper", Assert.Single(root.Elements("provider")).Element("software")?.Value);
    }

    [Fact]
    public void Duplicate_paths_in_input_keep_the_last_one_only()
    {
        var result = GamelistService.WriteOrMerge(GamelistPath,
        [
            Game("./Kirby.zip", "First", desc: "First description"),
            Game("./Zelda.zip", "Zelda"),
            Game("Kirby.zip", "Last"),
        ]);

        Assert.Equal(2, result.Written);
        XElement root = Root();
        // Le dernier gagne entièrement (son path, son nom, pas de description héritée du premier),
        // mais il garde la position du premier dans la liste.
        Assert.Equal(["Kirby.zip", "./Zelda.zip"], Games(root).Select(g => Val(g, "path")));
        XElement kirby = GameAt(root, "Kirby.zip");
        Assert.Equal("Last", Val(kirby, "name"));
        Assert.Null(Val(kirby, "desc"));
    }

    // ── Fichier illisible ─────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("<gameList><game><path>./Kirby.zip</path>")]
    [InlineData("<?xml version=\"1.0\"?><catalog><game/></catalog>")]
    [InlineData("pas du xml")]
    public void Unreadable_file_is_backed_up_and_recreated(string content)
    {
        WriteExisting(content);

        var result = GamelistService.WriteOrMerge(GamelistPath, [Game("./Kirby.zip", "Kirby")]);

        Assert.False(result.Merged);
        Assert.Equal(1, result.Written);
        string backup = result.BackupPath ?? throw new InvalidOperationException("BackupPath attendu");
        Assert.Equal(_dir, Path.GetDirectoryName(backup));
        Assert.Matches(@"^gamelist\.xml\.bak-\d{8}-\d{6}$", Path.GetFileName(backup));
        Assert.Equal(content, File.ReadAllText(backup));

        XElement root = Root();
        Assert.NotNull(root.Element("provider"));
        Assert.Equal("Kirby", Val(Assert.Single(Games(root)), "name"));
    }

    [Fact]
    public void Backup_never_overwrites_an_existing_backup()
    {
        var now = new DateTime(2026, 9, 13, 10, 15, 0);
        string first  = GamelistPath + ".bak-20260913-101500";
        string second = first + "-2";
        File.WriteAllText(first,  "first backup");
        File.WriteAllText(second, "second backup");
        WriteExisting("broken");

        var result = GamelistService.WriteOrMerge(GamelistPath, [Game("./Kirby.zip", "Kirby")], now);

        Assert.Equal(first + "-3", result.BackupPath);
        Assert.Equal("broken", File.ReadAllText(first + "-3"));
        Assert.Equal("first backup",  File.ReadAllText(first));
        Assert.Equal("second backup", File.ReadAllText(second));
    }

    // ── Normalisation ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("./Kirby.zip",      "Kirby.zip")]
    [InlineData("Kirby.zip",        "Kirby.zip")]
    [InlineData(".\\Kirby.zip",     "Kirby.zip")]
    [InlineData("  ./a\\b/c.zip  ", "a/b/c.zip")]
    [InlineData("./",               "")]
    public void Normalize_path_strips_prefix_and_unifies_separators(string input, string expected)
        => Assert.Equal(expected, GamelistService.NormalizePath(input));
}
