using System.IO;
using RomStationRebase.Models;
using RomStationRebase.Services;
using RomStationRebase.ViewModels;
using Xunit;

namespace RomStationRebase.Tests;

/// <summary>Fichiers de sélection : lecture, écriture, rapprochement avec la bibliothèque et état « modifié » de la session.</summary>
public sealed class RebasePresetTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rsr-sel-" + Guid.NewGuid().ToString("N"));

    public RebasePresetTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* fichier temporaire : sans conséquence */ }
    }

    private string PathOf(string name) => Path.Combine(_dir, name);

    private static RebasePreset Sample() => new()
    {
        Settings = new RebasePresetSettings
        {
            TargetPath = @"E:\roms", ArchitectureId = "arkos_default", ArchitectureLabel = "ArkOS / dArkOS (Default)",
            ArchiveMode = "ExtractAll", ExtractLayout = "Subfolder", CopyCovers = true, GenerateGamelist = true,
            BackupGamelist = true, MetadataLanguage = "fr", DuplicatePolicy = "Overwrite",
            MaxParallelCopies = 10, RetryCount = 5, RetryDelaySeconds = 5,
        },
        Games =
        [
            new() { Rid = 4242, Title = "Ronin Blade", System = "Playstation", Extract = true },
            new() { Rid = 31995, Title = "Metal Slug", System = "Neo-Geo" },
        ],
    };

    // ── Lecture et écriture ───────────────────────────────────────────────

    [Fact]
    public void Saved_file_loads_back_with_settings_games_and_per_game_rules()
    {
        string path = PathOf("tests.rsrgp");
        RebasePresetService.Save(path, Sample());

        var loaded = RebasePresetService.Load(path);

        Assert.Equal(RebasePreset.CurrentVersion, loaded.Version);
        Assert.Equal("arkos_default", loaded.Settings.ArchitectureId);
        Assert.Equal("ExtractAll", loaded.Settings.ArchiveMode);
        Assert.Equal(10, loaded.Settings.MaxParallelCopies);
        Assert.Equal(2, loaded.Games.Count);
        Assert.True(loaded.Games[0].Extract);
        Assert.Null(loaded.Games[0].M3U);
        Assert.False(loaded.Games[1].HasOverride);
    }

    [Fact]
    public void File_stays_readable_by_hand_with_accents_and_without_null_rules()
    {
        var file = Sample();
        file.Games[1].Title = "Pokémon Donjon Mystère";
        string json = RebasePresetService.Serialize(file);

        Assert.Contains("\"format\": \"romstation-rebase-preset\"", json);
        Assert.Contains("Pokémon Donjon Mystère", json);
        Assert.DoesNotContain("\"m3U\": null", json);
        Assert.DoesNotContain("hasOverride", json);
    }

    [Fact]
    public void Foreign_json_and_broken_json_are_told_apart()
    {
        string foreign = PathOf("autre.json");
        File.WriteAllText(foreign, "{ \"architectures\": [] }");
        string broken = PathOf("casse.rsrgp");
        File.WriteAllText(broken, "{ \"format\": ");

        Assert.Equal(RebasePresetError.NotASelection,
            Assert.Throws<RebasePresetException>(() => RebasePresetService.Load(foreign)).Error);
        Assert.Equal(RebasePresetError.InvalidJson,
            Assert.Throws<RebasePresetException>(() => RebasePresetService.Load(broken)).Error);
        Assert.Equal(RebasePresetError.Unreadable,
            Assert.Throws<RebasePresetException>(() => RebasePresetService.Load(PathOf("absent.rsrgp"))).Error);
    }

    [Fact]
    public void Hand_edited_file_with_missing_blocks_still_loads()
    {
        string path = PathOf("minimal.rsrgp");
        File.WriteAllText(path, "{ \"format\": \"romstation-rebase-selection\", \"version\": 7, // écrit à la main\n \"games\": [ { \"rid\": 5 }, ] }");

        var loaded = RebasePresetService.Load(path);

        Assert.Equal(7, loaded.Version);
        Assert.NotNull(loaded.Settings);
        Assert.Equal(5, Assert.Single(loaded.Games).Rid);
    }

    [Theory]
    [InlineData(@"C:\x\tests", @"C:\x\tests.rsrgp")]
    [InlineData(@"C:\x\tests.json", @"C:\x\tests.rsrgp")]
    [InlineData(@"C:\x\tests.RSRGP", @"C:\x\tests.RSRGP")]
    [InlineData(@"C:\x\ancien.rsr", @"C:\x\ancien.rsrgp")]            // extension du temps du développement
    [InlineData(@"C:\x\ancien.rsr.json", @"C:\x\ancien.rsrgp")]
    public void Extension_is_completed_without_doubling(string typed, string expected)
        => Assert.Equal(expected, RebasePresetService.EnsureExtension(typed));

    // ── Rapprochement avec la bibliothèque ────────────────────────────────

    [Fact]
    public void Games_are_matched_by_romstation_id_then_by_title_and_the_rest_is_reported_missing()
    {
        var file = Sample();
        file.Games.Add(new RebasePresetGame { Rid = 999, Title = "Kirby", System = "Super Nintendo" });   // rid changé
        file.Games.Add(new RebasePresetGame { Rid = 1000, Title = "Jeu supprimé", System = "PSP" });       // disparu

        var library = new List<LibraryGameRef>
        {
            new(1, 4242, "Ronin Blade (titre renommé)", "Playstation"),
            new(2, 31995, "Metal Slug", "Neo-Geo"),
            new(3, 555, "kirby", "Super Nintendo"),
            new(4, 556, "Kirby", "GameBoy"),
        };

        var match = RebasePresetService.Match(file, library);

        Assert.Equal([1, 2, 3], match.Matched.Keys.OrderBy(k => k));
        Assert.True(match.Matched[1].Extract);
        Assert.Equal("Kirby", Assert.Single(match.MatchedByTitle).Title);
        Assert.Equal("Jeu supprimé", Assert.Single(match.Missing).Title);
    }

    [Fact]
    public void Ambiguous_title_is_not_guessed()
    {
        var file = new RebasePreset { Games = [new() { Rid = 1, Title = "Tetris", System = "GameBoy" }] };
        var library = new List<LibraryGameRef> { new(10, 50, "Tetris", "GameBoy"), new(11, 51, "Tetris", "GameBoy") };

        var match = RebasePresetService.Match(file, library);

        Assert.Empty(match.Matched);
        Assert.Single(match.Missing);
    }

    // ── Session : fichier courant et état « modifié » ─────────────────────

    private static GameItemViewModel Game(int id, int rid, string title, bool selected)
        => new(id, rid, title, "Playstation", null, null, false, true, 1, string.Empty, selected, false, () => { });

    [Fact]
    public void Session_is_dirty_only_when_games_settings_or_rules_differ_from_the_file()
    {
        var games = new List<GameItemViewModel> { Game(1, 4242, "Ronin Blade", true), Game(2, 77, "Wipeout", false) };
        var session = new RebasePresetSessionViewModel(() => games.Where(g => g.IsSelected).ToList(), () => new RebasePresetSettings());
        string path = PathOf("session.rsrgp");

        Assert.False(session.HasFile);
        session.RefreshDirty();
        Assert.False(session.IsDirty); // sans fichier, rien à comparer

        session.SaveTo(path);
        Assert.True(session.HasFile);
        Assert.False(session.IsDirty);
        Assert.Equal("session.rsrgp", session.DisplayText);

        games[1].IsSelected = true;
        session.RefreshDirty();
        Assert.True(session.IsDirty);
        Assert.Equal("session.rsrgp •", session.DisplayText);

        games[1].IsSelected = false;
        session.RefreshDirty();
        Assert.False(session.IsDirty); // revenu à l'état enregistré

        session.Capture(new RebasePresetSettings { ArchitectureId = "onion_default" },
            new Dictionary<int, GameRuleOverrides> { [4242] = new(null, null, true) });
        Assert.True(session.IsDirty);

        session.SaveTo(path);
        var loaded = RebasePresetService.Load(path);
        Assert.Equal("onion_default", loaded.Settings.ArchitectureId);
        Assert.True(Assert.Single(loaded.Games).Extract);
        Assert.NotNull(loaded.AppVersion);
    }

    [Fact]
    public void Adopting_a_file_with_missing_games_does_not_mark_the_session_dirty()
    {
        var games = new List<GameItemViewModel> { Game(1, 4242, "Ronin Blade", true) };
        var session = new RebasePresetSessionViewModel(() => games.Where(g => g.IsSelected).ToList(), () => new RebasePresetSettings());

        // Le fichier listait deux jeux, un seul a pu être coché : l'état appliqué devient la référence
        session.Adopt(PathOf("ouvert.rsrgp"), Sample().Settings,
            new Dictionary<int, GameRuleOverrides> { [4242] = new(null, null, true), [31995] = default });

        Assert.False(session.IsDirty);
        Assert.Single(session.Overrides);
        Assert.Equal("ExtractAll", session.Settings!.ArchiveMode);

        session.Close();
        Assert.False(session.HasFile);
        Assert.Null(session.Settings);
    }
}
