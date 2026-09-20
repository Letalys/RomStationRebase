using System.ComponentModel;
using System.IO;
using System.Text;
using RomStationRebase.Models;
using RomStationRebase.Services;
using RomStationRebase.ViewModels;
using Xunit;

namespace RomStationRebase.Tests;

/// <summary>Journal détaillé du rebase : fichier horodaté, suivi en direct, contenu écrit par un vrai rebase.</summary>
public sealed class RebaseLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rsr-log-" + Guid.NewGuid().ToString("N"));

    public RebaseLogTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    // ── Écriture ──────────────────────────────────────────────────────────

    [Fact]
    public void Log_file_is_named_after_the_start_time()
    {
        using var log = RebaseLogWriter.Create(new DateTime(2026, 9, 20, 10, 15, 2), _dir)!;
        Assert.Equal("RSR_20260920_101502.log", Path.GetFileName(log.FilePath));

        // Deux rebases dans la même seconde ne s'écrasent pas
        using var second = RebaseLogWriter.Create(new DateTime(2026, 9, 20, 10, 15, 2), _dir)!;
        Assert.Equal("RSR_20260920_101502_2.log", Path.GetFileName(second.FilePath));
    }

    [Fact]
    public void Each_line_carries_a_timestamp_a_level_and_the_message()
    {
        string path;
        using (var log = RebaseLogWriter.Create(DateTime.Now, _dir)!)
        {
            path = log.FilePath;
            log.Info("Rebase démarré");
            log.Warn("Jaquette non copiée");
            log.Error("ligne 1\r\nligne 2"); // une erreur système sur deux lignes donne deux lignes datées
        }

        string[] lines = File.ReadAllLines(path, Encoding.UTF8);
        Assert.Equal(4, lines.Length);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}\tINFO\tRebase démarré$", lines[0]);
        Assert.EndsWith("\tWARN\tJaquette non copiée", lines[1]);
        Assert.EndsWith("\tERROR\tligne 1", lines[2]);
        Assert.EndsWith("\tERROR\tligne 2", lines[3]);
    }

    [Fact]
    public void Only_the_most_recent_logs_are_kept()
    {
        for (int day = 1; day <= 5; day++)
            File.WriteAllText(Path.Combine(_dir, $"RSR_202609{day:D2}_120000.log"), "x");
        File.WriteAllText(Path.Combine(_dir, "notes.txt"), "pas un journal");

        RebaseLogWriter.Prune(_dir, keep: 2);

        var left = Directory.GetFiles(_dir).Select(p => Path.GetFileName(p)!).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(["RSR_20260904_120000.log", "RSR_20260905_120000.log", "notes.txt"], left);
        Assert.EndsWith("RSR_20260905_120000.log", RebaseLogWriter.LatestLog(_dir));
    }

    [Fact]
    public void Writing_after_the_end_is_silent()
    {
        var log = RebaseLogWriter.Create(DateTime.Now, _dir)!;
        log.Dispose();
        log.Info("trop tard"); // ne doit jamais faire échouer un rebase
    }

    // ── Suivi ─────────────────────────────────────────────────────────────

    [Fact]
    public void Reader_follows_a_file_while_it_is_being_written()
    {
        using var log = RebaseLogWriter.Create(DateTime.Now, _dir)!;
        var reader = new RebaseLogTailReader(log.FilePath);

        log.Info("première");
        var first = reader.ReadNew();
        Assert.Single(first);
        Assert.Equal("première", first[0].Message);
        Assert.Equal(RebaseLogLevel.Info, first[0].Level);
        Assert.Matches(@"^\d{2}:\d{2}:\d{2}\.\d{3}$", first[0].Time);

        Assert.Empty(reader.ReadNew()); // rien de neuf

        log.Warn("deuxième");
        log.Error("troisième");
        var next = reader.ReadNew();
        Assert.Equal(["deuxième", "troisième"], next.Select(l => l.Message));
        Assert.Equal([RebaseLogLevel.Warning, RebaseLogLevel.Error], next.Select(l => l.Level));
    }

    [Fact]
    public void Reader_waits_for_the_end_of_a_half_written_line()
    {
        string path = Path.Combine(_dir, "partial.log");
        var reader = new RebaseLogTailReader(path);

        File.AppendAllText(path, "2026-09-20 10:00:00.000\tINFO\tdébut de");
        Assert.Empty(reader.ReadNew());

        File.AppendAllText(path, " ligne\r\n");
        Assert.Equal("début de ligne", Assert.Single(reader.ReadNew()).Message);
    }

    [Fact]
    public void Reader_starts_over_when_the_file_is_replaced()
    {
        string path = Path.Combine(_dir, "replaced.log");
        File.WriteAllText(path, "2026-09-20 10:00:00.000\tINFO\tun\r\n2026-09-20 10:00:01.000\tINFO\tdeux\r\n");
        var reader = new RebaseLogTailReader(path);
        Assert.Equal(2, reader.ReadNew().Count);

        File.WriteAllText(path, "2026-09-20 11:00:00.000\tINFO\tneuf\r\n");
        var lines = reader.ReadNew();
        Assert.True(reader.WasReset);
        Assert.Equal("neuf", Assert.Single(lines).Message);
    }

    [Fact]
    public void A_foreign_line_is_shown_as_it_is()
    {
        var line = RebaseLogLine.Parse("texte libre sans horodatage");
        Assert.Equal(string.Empty, line.Time);
        Assert.Equal(RebaseLogLevel.Info, line.Level);
        Assert.Equal("texte libre sans horodatage", line.Message);
    }

    // ── Contenu écrit par un rebase ───────────────────────────────────────

    [Fact]
    public async Task A_rebase_logs_its_configuration_its_plan_and_every_step()
    {
        string source = Path.Combine(_dir, "game.zip");
        File.WriteAllBytes(source, new byte[2048]);
        string target = Path.Combine(_dir, "target");
        Directory.CreateDirectory(Path.Combine(target, "snes"));
        File.WriteAllText(Path.Combine(target, "snes", "Déjà là.zip"), "x");

        var plan = new RebasePlan
        {
            Games =
            [
                Game(1, "Super Metroid", source, "Super Metroid.zip"),
                Game(2, "Déjà là", source, "Déjà là.zip"),
                Game(3, "Perdu", Path.Combine(_dir, "absent.zip"), "Perdu.zip"),
            ],
        };

        string logPath;
        using (var log = RebaseLogWriter.Create(DateTime.Now, Path.Combine(_dir, "logs"))!)
        {
            logPath = log.FilePath;
            var options = new RebaseOptions
            {
                Plan = plan, TargetPath = target, RetryCount = 0, RetryDelaySeconds = 0, Log = log,
                Architecture = new ArchitectureEntry { Id = "test_arch", Label = "Architecture de test" },
            };
            RebaseLogReport.WriteHeader(log, new RebaseLogContext("1.3.0", @"C:\RomStation", @"C:\db", null,
                "Extraire selon l'architecture", "Automatique", CopyCovers: false, MetadataFileName: null, "fr", ConvertFiles: true), options);

            await new RebaseService().RunRebaseAsync(options, new Progress<RebaseProgress>(), CancellationToken.None);
        }

        var lines = new RebaseLogTailReader(logPath).ReadNew();
        string all = string.Join("\n", lines.Select(l => l.Message));

        Assert.Contains("RomStation Rebase 1.3.0", all);
        Assert.Contains("Architecture de test (test_arch)", all);
        Assert.Contains(target, all);
        Assert.Contains(source, all);                       // le plan cite le chemin complet de la source
        Assert.Contains("snes/Super Metroid.zip", all);     // et l'exécution, le fichier écrit
        Assert.Contains(lines, l => l.Message.StartsWith("Super Metroid") && l.Level == RebaseLogLevel.Info);
        Assert.Contains(lines, l => l.Message.StartsWith("Déjà là.zip"));                                  // doublon ignoré
        Assert.Contains(lines, l => l.Message.StartsWith("Perdu") && l.Level == RebaseLogLevel.Error);     // source absente
        Assert.True(File.Exists(Path.Combine(target, "snes", "Super Metroid.zip")));
    }

    private static RebaseGamePlan Game(int id, string title, string source, string fileName) => new()
    {
        GameId = id, Title = title, SystemName = "Super Nintendo", TargetFolder = "snes", BaseName = title,
        OutputKind = GameOutputKind.Copy, PlannedBytes = 2048,
        Files = [new RebaseFilePlan { SourcePath = source, Label = title, Kind = FileTransferKind.Copy, LaunchRelativePath = fileName, CopySize = 2048 }],
    };

    // ── Table des systèmes de l'éditeur d'architectures ───────────────────

    [Fact]
    public void Systems_sort_by_name_and_an_unnamed_row_stays_last()
    {
        var rows = new[] { "Playstation", "", "dreamcast", "Game Boy" }
            .Select(n => new SystemMappingRowViewModel(new SystemMapping { RomStationSystem = n })).ToList();

        var asc = rows.ToList();
        asc.Sort((a, b) => new ArchitectureEditorViewModel.SystemNameComparer(ListSortDirection.Ascending).Compare(a, b));
        Assert.Equal(["dreamcast", "Game Boy", "Playstation", ""], asc.Select(r => r.RomStationSystem));

        var desc = rows.ToList();
        desc.Sort((a, b) => new ArchitectureEditorViewModel.SystemNameComparer(ListSortDirection.Descending).Compare(a, b));
        Assert.Equal(["Playstation", "Game Boy", "dreamcast", ""], desc.Select(r => r.RomStationSystem));
    }

    [Fact]
    public void A_row_shows_the_icon_of_its_system_and_follows_a_rename()
    {
        var icons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Dreamcast"]   = @"C:\icons\dc.png",
            ["Playstation"] = @"C:\icons\psx.png",
        };
        var draft = new ArchitectureDraftViewModel(new ArchitectureEntry { Id = "t", Label = "T" }) { SystemIcons = icons };
        draft.LoadMappings(new FolderTreeMapping
        {
            FolderTreeMappings = [new SystemMapping { RomStationSystem = "dreamcast", TargetFolder = "dreamcast" }],
        });

        var row = draft.Mappings[0];
        Assert.Equal(@"C:\icons\dc.png", row.IconPath);

        var changed = new List<string?>();
        row.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        row.RomStationSystem = "Playstation";
        Assert.Equal(@"C:\icons\psx.png", row.IconPath);
        Assert.Contains(nameof(SystemMappingRowViewModel.IconPath), changed);

        row.RomStationSystem = "Système inconnu";
        Assert.Null(row.IconPath);

        // Une ligne ajoutée reçoit les icônes comme les autres
        draft.Mappings.Add(new SystemMappingRowViewModel { RomStationSystem = "Playstation" });
        Assert.Equal(@"C:\icons\psx.png", draft.Mappings[1].IconPath);
    }
}
