using System.IO;
using RomStationRebase.Models;
using RomStationRebase.Services;
using Xunit;

namespace RomStationRebase.Tests;

/// <summary>Fige les règles de nommage de la 1.3.0 sur les cas réels relevés dans la bibliothèque du 2026-09-12.</summary>
public class RebasePlannerTests
{
    private const string RsPath = @"J:\RomStation";

    private static readonly FolderTreeMapping ArkOsMapping = new()
    {
        FolderTreeMappings =
        [
            new() { RomStationSystem = "Playstation",    TargetFolder = "psx",    M3U = true },
            new() { RomStationSystem = "GameCube",       TargetFolder = "gc",     M3U = true, Extract = true },
            new() { RomStationSystem = "PSP",            TargetFolder = "psp",    Extract = true },
            new() { RomStationSystem = "Neo-Geo",        TargetFolder = "neogeo", KeepFileName = true },
            new() { RomStationSystem = "Super Nintendo", TargetFolder = "snes" },
            new() { RomStationSystem = "GameBoy Advance",TargetFolder = "gba" },
            new() { RomStationSystem = "Megadrive",      TargetFolder = "megadrive" },
            new() { RomStationSystem = "DOS",            TargetFolder = "dos" },
        ],
    };

    private static readonly ArchitectureEntry ArkOs = new()
    {
        Id = "retroarch", CoverFolder = "images", CoverSuffix = "-image", GamelistFormat = "emulationstation",
    };

    private static GameFileInfo File(int gameId, int fileId, string label, string relPath)
        => new() { GameId = gameId, FileId = fileId, Label = label, RelativePath = relPath,
                   RelativeDirectory = Path.GetDirectoryName(relPath) ?? string.Empty };

    private static PlanGameInput Game(int id, string title, string system, int rid, bool cover = true)
        => new(id, title, system, null, rid, cover ? $@"J:\RomStation\app\games\downloads\{title} - {rid}\images\cover.png" : null, cover);

    private sealed class Builder
    {
        public List<PlanGameInput> All = new();
        public List<PlanGameInput> Selected = new();
        public Dictionary<int, IReadOnlyList<GameFileInfo>> Files = new();
        public Dictionary<string, ArchiveInfo> Archives = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<(string, long)>> Dirs = new(StringComparer.OrdinalIgnoreCase);
        public bool M3U = true, Covers = true, Gamelist = true;
        public ArchiveMode Mode = ArchiveMode.ExtractRequired;
        public ExtractLayout Layout = ExtractLayout.Auto;
        public ArchitectureEntry Arch = ArkOs;

        public Builder Add(PlanGameInput g, bool select, params GameFileInfo[] files)
        {
            All.Add(g);
            if (select) Selected.Add(g);
            Files[g.Id] = files;
            return this;
        }

        public Builder Archive(string relPath, params (string name, long size)[] entries)
        {
            string abs = Path.Combine(RsPath, "app", relPath);
            Archives[abs] = new ArchiveInfo
            {
                Path = abs, CompressedSize = 100, IsReadable = true,
                UncompressedSize = entries.Sum(e => e.size),
                Entries = entries.Select(e => new ArchiveEntryInfo { InternalPath = e.name, FileName = Path.GetFileName(e.name), Length = e.size }).ToList(),
            };
            return this;
        }

        public RebasePlan Plan() => new RebasePlanner().Plan(new RebasePlanRequest
        {
            SelectedGames        = Selected,
            AllGames             = All,
            FilesByGame          = Files,
            ArchiveLookup        = p => Archives.TryGetValue(p, out var a) ? a : null,
            FileSizeLookup       = _ => 100,
            DirectoryFilesLookup = d => Dirs.TryGetValue(d, out var l) ? l : new List<(string, long)> { ("x.zip", 100) },
            RomStationPath       = RsPath,
            Mapping              = ArkOsMapping,
            Architecture         = Arch,
            GenerateM3U          = M3U,
            ArchiveMode          = Mode,
            Layout               = Layout,
            CopyCovers           = Covers,
            GenerateGamelist     = Gamelist,
        });
    }

    // ── Romsets arcade ────────────────────────────────────────────────────

    [Fact]
    public void Arcade_romset_keeps_its_original_file_name()
    {
        var plan = new Builder()
            .Add(Game(1, "Metal Slug", "Neo-Geo", 31995), true,
                 File(1, 1, "Metal Slug", @"games\downloads\Metal Slug - 31995\files\12889\mslug.zip"))
            .Plan();

        var g = Assert.Single(plan.Games);
        Assert.Equal(GameOutputKind.Romset, g.OutputKind);
        Assert.Equal("mslug.zip", g.Files[0].LaunchRelativePath);
        Assert.Equal(FileTransferKind.Copy, g.Files[0].Kind);
        Assert.Null(g.M3URelativePath);
        Assert.Equal("./mslug.zip", g.GamelistEntries[0].Path);
        Assert.Equal("Metal Slug", g.GamelistEntries[0].Name);
        Assert.Equal("images/mslug-image.png", g.Covers[0].DestRelativePath);
    }

    [Fact]
    public void Arcade_romset_is_never_extracted_even_in_extract_all_mode()
    {
        var b = new Builder { Mode = ArchiveMode.ExtractAll }
            .Add(Game(1, "Metal Slug", "Neo-Geo", 31995), true,
                 File(1, 1, "Metal Slug", @"games\downloads\Metal Slug - 31995\files\12889\mslug.zip"))
            .Archive(@"games\downloads\Metal Slug - 31995\files\12889\mslug.zip", ("201-c1.c1", 4194304));

        var g = b.Plan().Games[0];
        Assert.Equal(FileTransferKind.Copy, g.Files[0].Kind);
        Assert.Equal("mslug.zip", g.Files[0].LaunchRelativePath);
    }

    // ── Fichier unique : nom 1.2.0 conservé ───────────────────────────────

    [Fact]
    public void Single_file_game_keeps_the_1_2_0_naming()
    {
        var g = new Builder()
            .Add(Game(10, "Kirby : Nightmare in Dream Land", "GameBoy Advance", 555), true,
                 File(10, 1, "Kirby : Nightmare in Dream Land", @"games\downloads\Kirby - 555\files\1\555.zip"))
            .Plan().Games[0];

        Assert.Equal("Kirby - Nightmare in Dream Land.zip", g.Files[0].LaunchRelativePath);
        Assert.Equal(GameOutputKind.Copy, g.OutputKind);
        Assert.Equal("images/Kirby - Nightmare in Dream Land-image.png", g.Covers[0].DestRelativePath);
        Assert.Equal("./images/Kirby - Nightmare in Dream Land-image.png", g.GamelistEntries[0].Image);
    }

    // ── Disques ───────────────────────────────────────────────────────────

    [Fact]
    public void Disc_set_gets_disc_names_ordered_and_an_m3u()
    {
        var g = new Builder()
            .Add(Game(174, "Chrono Cross", "Playstation", 664), true,
                 File(174, 181, "Chrono Cross (Disc 2)", @"games\downloads\Chrono Cross - 664\files\1347\664b.zip"),
                 File(174, 180, "Chrono Cross (Disc 1)", @"games\downloads\Chrono Cross - 664\files\1345\664a.zip"))
            .Plan().Games[0];

        Assert.Equal(GameOutputKind.DiscSet, g.OutputKind);
        Assert.Equal(["Chrono Cross (Disc 1).zip", "Chrono Cross (Disc 2).zip"], g.Files.Select(f => f.LaunchRelativePath));
        Assert.Equal("Chrono Cross.m3u", g.M3URelativePath);
        Assert.Equal(["Chrono Cross (Disc 1).zip", "Chrono Cross (Disc 2).zip"], g.M3UEntries);
        var entry = Assert.Single(g.GamelistEntries);
        Assert.Equal("./Chrono Cross.m3u", entry.Path);
        Assert.Equal("Chrono Cross", entry.Name);
        Assert.Equal("images/Chrono Cross-image.png", Assert.Single(g.Covers).DestRelativePath);
    }

    [Fact]
    public void Disc_set_without_m3u_lists_each_disc_in_the_gamelist()
    {
        var g = new Builder { M3U = false }
            .Add(Game(174, "Chrono Cross", "Playstation", 664), true,
                 File(174, 180, "Chrono Cross (Disc 1)", @"games\downloads\Chrono Cross - 664\files\1345\664a.zip"),
                 File(174, 181, "Chrono Cross (Disc 2)", @"games\downloads\Chrono Cross - 664\files\1347\664b.zip"))
            .Plan().Games[0];

        Assert.Equal(GameOutputKind.Discs, g.OutputKind);
        Assert.Null(g.M3URelativePath);
        Assert.Equal(2, g.GamelistEntries.Count);
        Assert.Equal("Chrono Cross (Disc 1)", g.GamelistEntries[0].Name);
        Assert.Equal("./Chrono Cross (Disc 2).zip", g.GamelistEntries[1].Path);
        Assert.Equal(2, g.Covers.Count);
    }

    [Fact]
    public void Disc_marker_with_extra_qualifiers_is_still_a_disc()
    {
        var g = new Builder()
            .Add(Game(109, "Lunar: Silver Star Story Complete", "Playstation", 82198), true,
                 File(109, 1, "Lunar: Silver Star Story Complete (Disc 1) (v1.01)", @"games\downloads\Lunar - 82198\files\1\a.zip"),
                 File(109, 2, "Lunar: Silver Star Story Complete (Disc 2) (v1.01)", @"games\downloads\Lunar - 82198\files\2\b.zip"))
            .Plan().Games[0];

        Assert.Equal(GameOutputKind.DiscSet, g.OutputKind);
        Assert.Equal("Lunar- Silver Star Story Complete (Disc 1).zip", g.Files[0].LaunchRelativePath);
    }

    // ── Versions d'un même jeu ────────────────────────────────────────────

    [Fact]
    public void Multiple_versions_are_named_after_their_label_without_m3u()
    {
        var g = new Builder()
            .Add(Game(146, "Breath of Fire IV", "Playstation", 34634), true,
                 File(146, 149, "Breath of Fire IV (v1.0a NTSC)", @"games\downloads\BoF - 34634\files\64303\34634_64303.zip"),
                 File(146, 150, "Breath of Fire IV (v1.0a PAL)",  @"games\downloads\BoF - 34634\files\64302\34634_64302.zip"))
            .Plan().Games[0];

        Assert.Equal(GameOutputKind.Variants, g.OutputKind);
        Assert.Null(g.M3URelativePath);
        Assert.Equal("Breath of Fire IV (v1.0a NTSC).zip", g.Files[0].LaunchRelativePath);
        Assert.Equal("Breath of Fire IV (v1.0a PAL).zip",  g.Files[1].LaunchRelativePath);
        Assert.Equal("Breath of Fire IV (v1.0a PAL)", g.GamelistEntries[1].Name);
    }

    [Fact]
    public void Gdi_and_nod_discs_are_versions_not_a_disc_set()
    {
        var g = new Builder()
            .Add(Game(113, "Command & Conquer", "Playstation", 74102), true,
                 File(113, 114, "Command & Conquer (GDI Disc)", @"games\downloads\CC - 74102\files\58132\a.zip"),
                 File(113, 115, "Command & Conquer (NOD Disc)", @"games\downloads\CC - 74102\files\58134\b.zip"))
            .Plan().Games[0];

        Assert.Equal(GameOutputKind.Variants, g.OutputKind);
        Assert.Equal("Command & Conquer (GDI Disc).zip", g.Files[0].LaunchRelativePath);
    }

    // ── Homonymes ─────────────────────────────────────────────────────────

    [Fact]
    public void Homonyms_are_told_apart_by_their_derby_label_when_it_differs()
    {
        var plan = new Builder()
            .Add(Game(1822, "Metroid Fusion", "GameBoy Advance", 72188), true,
                 File(1822, 1, "Metroid Fusion (V 1.0)", @"games\downloads\Metroid Fusion - 72188\files\55877\72188_55877.zip"))
            .Add(Game(1823, "Metroid Fusion", "GameBoy Advance", 609), true,
                 File(1823, 2, "Metroid Fusion", @"games\downloads\Metroid Fusion - 609\files\1211\609.zip"))
            .Plan();

        Assert.Equal("Metroid Fusion (V 1.0).zip", plan.Games[0].Files[0].LaunchRelativePath);
        Assert.Equal("Metroid Fusion.zip",         plan.Games[1].Files[0].LaunchRelativePath);
    }

    [Fact]
    public void Homonyms_with_identical_labels_get_the_romstation_id()
    {
        var plan = new Builder()
            .Add(Game(1372, "Super Metroid", "Super Nintendo", 79038), true,
                 File(1372, 1, "Super Metroid", @"games\downloads\Super Metroid - 79038\files\64505\79038_64505.zip"))
            .Add(Game(1398, "Super Metroid", "Super Nintendo", 116), true,
                 File(1398, 2, "Super Metroid", @"games\downloads\Super Metroid - 116\files\231\116.zip"))
            .Plan();

        Assert.Equal("Super Metroid (RS-79038).zip", plan.Games[0].Files[0].LaunchRelativePath);
        Assert.Equal("Super Metroid (RS-116).zip",   plan.Games[1].Files[0].LaunchRelativePath);
    }

    [Fact]
    public void Homonym_names_are_stable_even_when_only_one_of_them_is_selected()
    {
        var plan = new Builder()
            .Add(Game(1372, "Super Metroid", "Super Nintendo", 79038), true,
                 File(1372, 1, "Super Metroid", @"games\downloads\Super Metroid - 79038\files\64505\79038_64505.zip"))
            .Add(Game(1398, "Super Metroid", "Super Nintendo", 116), false,
                 File(1398, 2, "Super Metroid", @"games\downloads\Super Metroid - 116\files\231\116.zip"))
            .Plan();

        Assert.Equal("Super Metroid (RS-79038).zip", Assert.Single(plan.Games).Files[0].LaunchRelativePath);
    }

    [Fact]
    public void Same_title_on_different_systems_is_not_a_collision()
    {
        var plan = new Builder()
            .Add(Game(1, "Aladdin", "GameBoy Advance", 1), true, File(1, 1, "Aladdin", @"games\downloads\A - 1\files\1\1.zip"))
            .Add(Game(2, "Aladdin", "Megadrive", 2), true,       File(2, 2, "Aladdin", @"games\downloads\A - 2\files\2\2.zip"))
            .Plan();

        Assert.All(plan.Games, g => Assert.Equal("Aladdin.zip", g.Files[0].LaunchRelativePath));
    }

    // ── Extraction ────────────────────────────────────────────────────────

    [Fact]
    public void Single_entry_archive_is_extracted_flat_and_renamed()
    {
        var g = new Builder()
            .Add(Game(2285, "Burnout Legends", "PSP", 90802), true,
                 File(2285, 1, "Burnout Legends", @"games\downloads\Burnout Legends - 90802\files\80687\90802_80687.zip"))
            .Archive(@"games\downloads\Burnout Legends - 90802\files\80687\90802_80687.zip", ("ULES00125.iso", 500))
            .Plan().Games[0];

        var f = g.Files[0];
        Assert.Equal(GameOutputKind.Extract, g.OutputKind);
        Assert.Equal(FileTransferKind.Extract, f.Kind);
        Assert.Equal("", f.DestRelativeDir);
        Assert.Equal("Burnout Legends.iso", f.LaunchRelativePath);
        Assert.Equal("Burnout Legends.iso", f.SingleEntryTargetName);
        Assert.Equal(500, f.PlannedBytes);
        Assert.Equal("images/Burnout Legends-image.png", g.Covers[0].DestRelativePath);
    }

    [Fact]
    public void Multi_entry_archive_is_extracted_into_a_subfolder_keeping_internal_names()
    {
        var g = new Builder { Mode = ArchiveMode.ExtractAll }
            .Add(Game(174, "Chrono Cross", "Playstation", 664), true,
                 File(174, 180, "Chrono Cross (Disc 1)", @"games\downloads\Chrono Cross - 664\files\1345\664a.zip"),
                 File(174, 181, "Chrono Cross (Disc 2)", @"games\downloads\Chrono Cross - 664\files\1347\664b.zip"))
            .Archive(@"games\downloads\Chrono Cross - 664\files\1345\664a.zip", ("Chrono Cross(Disque 1).bin", 700), ("Chrono Cross(Disque 1).cue", 1))
            .Archive(@"games\downloads\Chrono Cross - 664\files\1347\664b.zip", ("Chrono Cross(Disque 2).bin", 700), ("Chrono Cross(Disque 2).cue", 1))
            .Plan().Games[0];

        Assert.Equal(GameOutputKind.DiscSet, g.OutputKind);
        Assert.Equal("Chrono Cross (Disc 1)", g.Files[0].DestRelativeDir);
        Assert.Equal("Chrono Cross (Disc 1)/Chrono Cross(Disque 1).cue", g.Files[0].LaunchRelativePath);
        Assert.Null(g.Files[0].SingleEntryTargetName);
        Assert.Equal(["Chrono Cross (Disc 1)/Chrono Cross(Disque 1).cue", "Chrono Cross (Disc 2)/Chrono Cross(Disque 2).cue"], g.M3UEntries);
        Assert.Equal(1402, g.Files.Sum(f => f.PlannedBytes));
    }

    [Fact]
    public void Subfolder_layout_forces_a_folder_even_for_a_single_entry()
    {
        var g = new Builder { Layout = ExtractLayout.Subfolder }
            .Add(Game(2285, "Burnout Legends", "PSP", 90802), true,
                 File(2285, 1, "Burnout Legends", @"games\downloads\Burnout Legends - 90802\files\80687\90802_80687.zip"))
            .Archive(@"games\downloads\Burnout Legends - 90802\files\80687\90802_80687.zip", ("ULES00125.iso", 500))
            .Plan().Games[0];

        Assert.Equal("Burnout Legends", g.Files[0].DestRelativeDir);
        Assert.Equal("Burnout Legends/ULES00125.iso", g.Files[0].LaunchRelativePath);
        Assert.Equal("images/Burnout Legends-image.png", g.Covers[0].DestRelativePath);
    }

    [Fact]
    public void Extraction_is_skipped_for_systems_that_read_archives_in_required_mode()
    {
        var g = new Builder()
            .Add(Game(174, "Chrono Cross", "Playstation", 664), true,
                 File(174, 180, "Chrono Cross (Disc 1)", @"games\downloads\Chrono Cross - 664\files\1345\664a.zip"),
                 File(174, 181, "Chrono Cross (Disc 2)", @"games\downloads\Chrono Cross - 664\files\1347\664b.zip"))
            .Archive(@"games\downloads\Chrono Cross - 664\files\1345\664a.zip", ("a.bin", 700), ("a.cue", 1))
            .Plan().Games[0];

        Assert.All(g.Files, f => Assert.Equal(FileTransferKind.Copy, f.Kind));
    }

    [Fact]
    public void Copy_mode_never_extracts()
    {
        var g = new Builder { Mode = ArchiveMode.Copy }
            .Add(Game(2285, "Burnout Legends", "PSP", 90802), true,
                 File(2285, 1, "Burnout Legends", @"games\downloads\Burnout Legends - 90802\files\80687\90802_80687.zip"))
            .Archive(@"games\downloads\Burnout Legends - 90802\files\80687\90802_80687.zip", ("ULES00125.iso", 500))
            .Plan().Games[0];

        Assert.Equal(FileTransferKind.Copy, g.Files[0].Kind);
        Assert.Equal("Burnout Legends.zip", g.Files[0].LaunchRelativePath);
    }

    [Fact]
    public void Unreadable_archive_falls_back_to_copy()
    {
        var g = new Builder()
            .Add(Game(2285, "Burnout Legends", "PSP", 90802), true,
                 File(2285, 1, "Burnout Legends", @"games\downloads\Burnout Legends - 90802\files\80687\90802_80687.zip"))
            .Plan().Games[0];

        Assert.Equal(FileTransferKind.Copy, g.Files[0].Kind);
    }

    // ── Jeux en dossier (DOS) ─────────────────────────────────────────────

    [Fact]
    public void Installed_folder_game_is_copied_as_a_tree()
    {
        var b = new Builder()
            .Add(Game(103, "TerraFire", "DOS", 62381), true,
                 File(103, 1, "TerraFire", @"games\downloads\TerraFire - 62381\files\44117\dosbox.conf"));
        b.Dirs[Path.Combine(RsPath, "app", @"games\downloads\TerraFire - 62381\files\44117")] =
            [("dosbox.conf", 10), ("TerraFir/TERRA.EXE", 1000), ("TerraFir/DATA/x.dat", 5000)];

        var g = b.Plan().Games[0];
        Assert.Equal(GameOutputKind.Folder, g.OutputKind);
        Assert.Equal(FileTransferKind.CopyTree, g.Files[0].Kind);
        Assert.Equal("TerraFire", g.Files[0].DestRelativeDir);
        Assert.Equal("TerraFire/dosbox.conf", g.Files[0].LaunchRelativePath);
        Assert.Equal(6010, g.Files[0].PlannedBytes);
        Assert.Equal("./TerraFire/dosbox.conf", g.GamelistEntries[0].Path);
        Assert.Equal("images/TerraFire-image.png", g.Covers[0].DestRelativePath);
    }

    // ── Surcharges par jeu ────────────────────────────────────────────────

    [Fact]
    public void Per_game_overrides_replace_the_architecture_rules_for_that_game_only()
    {
        // PSP exige l'extraction dans l'architecture, ce jeu la refuse ; Playstation lit le M3U, ce jeu le refuse
        var b = new Builder()
            .Add(Game(2285, "Burnout Legends", "PSP", 90802) with { ExtractOverride = false }, true,
                 File(2285, 1, "Burnout Legends", @"games\downloads\Burnout Legends - 90802\files\80687\90802_80687.zip"))
            .Add(Game(174, "Chrono Cross", "Playstation", 664) with { M3UOverride = false }, true,
                 File(174, 180, "Chrono Cross (Disc 1)", @"games\downloads\Chrono Cross - 664\files\1345\664a.zip"),
                 File(174, 181, "Chrono Cross (Disc 2)", @"games\downloads\Chrono Cross - 664\files\1347\664b.zip"))
            .Add(Game(10, "Kirby", "GameBoy Advance", 555) with { KeepFileNameOverride = true }, true,
                 File(10, 1, "Kirby", @"games\downloads\Kirby - 555\files\1\555.zip"))
            .Archive(@"games\downloads\Burnout Legends - 90802\files\80687\90802_80687.zip", ("ULES00125.iso", 500));

        var plan = b.Plan();
        Assert.Equal(FileTransferKind.Copy, plan.Games[0].Files[0].Kind);
        Assert.Equal(GameOutputKind.Discs, plan.Games[1].OutputKind);
        Assert.Null(plan.Games[1].M3URelativePath);
        Assert.Equal(GameOutputKind.Romset, plan.Games[2].OutputKind);
        Assert.Equal("555.zip", plan.Games[2].Files[0].LaunchRelativePath);
    }

    // ── Systèmes sans correspondance, options ─────────────────────────────

    [Fact]
    public void Unmapped_system_is_flagged_and_produces_nothing()
    {
        var g = new Builder()
            .Add(Game(2400, "Pandora's Tower", "Wii", 72963), true,
                 File(2400, 1, "Pandora's Tower", @"games\downloads\PT - 72963\files\56828\72963_56828.zip"))
            .Plan().Games[0];

        Assert.True(g.IsUnmapped);
        Assert.Equal(GameOutputKind.Unmapped, g.OutputKind);
        Assert.Empty(g.Files);
        Assert.Empty(g.Covers);
    }

    [Fact]
    public void Covers_and_gamelist_are_omitted_when_disabled_or_cover_missing()
    {
        var g = new Builder { Covers = false, Gamelist = false }
            .Add(Game(10, "Kirby", "GameBoy Advance", 555, cover: false), true,
                 File(10, 1, "Kirby", @"games\downloads\Kirby - 555\files\1\555.zip"))
            .Plan().Games[0];

        Assert.Empty(g.Covers);
        Assert.Empty(g.GamelistEntries);
    }

    [Fact]
    public void Output_paths_list_everything_written_under_the_destination()
    {
        var g = new Builder()
            .Add(Game(174, "Chrono Cross", "Playstation", 664), true,
                 File(174, 180, "Chrono Cross (Disc 1)", @"games\downloads\Chrono Cross - 664\files\1345\664a.zip"),
                 File(174, 181, "Chrono Cross (Disc 2)", @"games\downloads\Chrono Cross - 664\files\1347\664b.zip"))
            .Plan().Games[0];

        Assert.Equal(
            ["psx/Chrono Cross (Disc 1).zip", "psx/Chrono Cross (Disc 2).zip", "psx/Chrono Cross.m3u", "psx/images/Chrono Cross-image.png"],
            g.OutputPaths);
    }

    // ── Image disque livrée sans descripteur ──────────────────────────────

    private const long DiscSize = 400L * 1024 * 1024;

    [Fact]
    public void Lone_bin_disc_image_gets_a_cue_sheet_that_becomes_the_launch_file()
    {
        // Cas réel : Ronin Blade (Playstation), sorti en .bin seul et invisible sous dArkOS
        var g = new Builder { Mode = ArchiveMode.ExtractAll }
            .Add(Game(10, "Ronin Blade", "Playstation", 4242), true,
                 File(10, 1, "Ronin Blade", @"games\downloads\Ronin Blade - 4242\files\1\ronin.zip"))
            .Archive(@"games\downloads\Ronin Blade - 4242\files\1\ronin.zip", ("SLES_024.13.bin", DiscSize))
            .Plan().Games[0];

        var f = g.Files[0];
        Assert.Equal("Ronin Blade.bin", f.SingleEntryTargetName);
        Assert.Equal("Ronin Blade.cue", f.CueRelativePath);
        Assert.Equal("Ronin Blade.bin", f.CueBinRelativePath);
        Assert.Equal("Ronin Blade.cue", f.LaunchRelativePath);
        Assert.Equal("./Ronin Blade.cue", g.GamelistEntries[0].Path);
        Assert.Equal("images/Ronin Blade-image.png", g.Covers[0].DestRelativePath);
        Assert.Contains("psx/Ronin Blade.cue", g.OutputPaths);
        Assert.Contains("psx/Ronin Blade.bin", g.OutputPaths);
    }

    [Fact]
    public void Lone_bin_in_a_subfolder_gets_its_cue_next_to_it()
    {
        var g = new Builder { Mode = ArchiveMode.ExtractAll, Layout = ExtractLayout.Subfolder }
            .Add(Game(10, "Ronin Blade", "Playstation", 4242), true,
                 File(10, 1, "Ronin Blade", @"games\downloads\Ronin Blade - 4242\files\1\ronin.zip"))
            .Archive(@"games\downloads\Ronin Blade - 4242\files\1\ronin.zip", ("SLES_024.13.bin", DiscSize))
            .Plan().Games[0];

        var f = g.Files[0];
        Assert.Equal("Ronin Blade/SLES_024.13.cue", f.CueRelativePath);
        Assert.Equal("Ronin Blade/SLES_024.13.bin", f.CueBinRelativePath);
        Assert.Equal("Ronin Blade/SLES_024.13.cue", f.LaunchRelativePath);
    }

    [Fact]
    public void Cartridge_bin_is_never_given_a_cue_sheet()
    {
        // Une ROM Megadrive porte aussi l'extension .bin : sa taille la distingue d'une image disque
        var g = new Builder { Mode = ArchiveMode.ExtractAll }
            .Add(Game(11, "Sonic", "Megadrive", 77), true,
                 File(11, 1, "Sonic", @"games\downloads\Sonic - 77\files\1\sonic.zip"))
            .Archive(@"games\downloads\Sonic - 77\files\1\sonic.zip", ("sonic.bin", 512 * 1024))
            .Plan().Games[0];

        Assert.Null(g.Files[0].CueRelativePath);
        Assert.Equal("Sonic.bin", g.Files[0].LaunchRelativePath);
    }

    [Fact]
    public void Archive_that_ships_its_own_cue_is_left_alone()
    {
        var g = new Builder { Mode = ArchiveMode.ExtractAll }
            .Add(Game(12, "Wipeout", "Playstation", 88), true,
                 File(12, 1, "Wipeout", @"games\downloads\Wipeout - 88\files\1\w.zip"))
            .Archive(@"games\downloads\Wipeout - 88\files\1\w.zip", ("w.cue", 100), ("w.bin", DiscSize))
            .Plan().Games[0];

        Assert.Null(g.Files[0].CueRelativePath);
        Assert.Equal("Wipeout/w.cue", g.Files[0].LaunchRelativePath);
    }

    // ── Jaquettes et métadonnées hors de l'arborescence des ROMs (ES-DE, Cocoon) ──

    private static readonly ArchitectureEntry EsDe = new()
    {
        Id = "esde", CoverFolder = "ES-DE/downloaded_media/{system}/covers", CoverSuffix = "",
        GamelistFormat = MetadataFormats.EsDe,
    };

    [Fact]
    public void EsDe_covers_leave_the_rom_tree_and_the_gamelist_carries_no_image()
    {
        var g = new Builder { Arch = EsDe }
            .Add(Game(20, "Kirby", "Super Nintendo", 5), true,
                 File(20, 1, "Kirby", @"games\downloads\Kirby - 5\files\1\kirby.zip"))
            .Plan().Games[0];

        var cover = Assert.Single(g.Covers);
        Assert.True(cover.IsRootRelative);
        Assert.Equal("ES-DE/downloaded_media/snes/covers/Kirby.png", cover.DestRelativePath);
        Assert.Null(g.GamelistEntries[0].Image);
        Assert.Contains("ES-DE/downloaded_media/snes/covers/Kirby.png", g.OutputPaths);
    }

    [Fact]
    public void EsDe_cover_mirrors_the_subfolder_of_the_launch_file()
    {
        // ES-DE cherche covers/Wipeout/w.png pour un jeu lancé par Wipeout/w.cue
        var g = new Builder { Arch = EsDe, Mode = ArchiveMode.ExtractAll }
            .Add(Game(12, "Wipeout", "Playstation", 88), true,
                 File(12, 1, "Wipeout", @"games\downloads\Wipeout - 88\files\1\w.zip"))
            .Archive(@"games\downloads\Wipeout - 88\files\1\w.zip", ("w.cue", 100), ("w.bin", DiscSize))
            .Plan().Games[0];

        Assert.Equal("ES-DE/downloaded_media/psx/covers/Wipeout/w.png", g.Covers[0].DestRelativePath);
    }

    [Fact]
    public void Root_relative_covers_with_an_image_tag_climb_out_of_the_system_folder()
    {
        var arch = new ArchitectureEntry
        {
            Id = "custom", CoverFolder = "media/{system}/box", CoverSuffix = "",
            GamelistFormat = MetadataFormats.EmulationStation,
        };
        var g = new Builder { Arch = arch }
            .Add(Game(20, "Kirby", "Super Nintendo", 5), true,
                 File(20, 1, "Kirby", @"games\downloads\Kirby - 5\files\1\kirby.zip"))
            .Plan().Games[0];

        Assert.Equal("../media/snes/box/Kirby.png", g.GamelistEntries[0].Image);
    }

    [Fact]
    public void Unknown_metadata_format_is_treated_as_none()
    {
        var arch = new ArchitectureEntry { Id = "x", CoverFolder = "images", GamelistFormat = "format-du-futur" };
        Assert.False(arch.SupportsGamelist);

        var g = new Builder { Arch = arch }
            .Add(Game(20, "Kirby", "Super Nintendo", 5), true,
                 File(20, 1, "Kirby", @"games\downloads\Kirby - 5\files\1\kirby.zip"))
            .Plan().Games[0];
        Assert.Empty(g.GamelistEntries);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("a/b.c/game.cue", "a/b.c/game")]
    [InlineData("Game v1.2.iso", "Game v1.2")]
    [InlineData("dir.x/file", "dir.x/file")]
    public void Extension_is_stripped_from_the_last_segment_only(string path, string expected)
        => Assert.Equal(expected, RebasePlanner.WithoutExtension(path));

    [Theory]
    [InlineData("Chrono Cross (Disc 1)", 1)]
    [InlineData("D (Disc 3)", 3)]
    [InlineData("Chrono Cross (Disque 2)", 2)]
    [InlineData("Xenogears (Disc 2) 1.1", 2)]
    [InlineData("Breath of Fire IV (v1.0a PAL)", null)]
    [InlineData("Command & Conquer (GDI Disc)", null)]
    public void Disc_number_is_read_from_the_label(string label, int? expected)
        => Assert.Equal(expected, RebasePlanner.DiscNumber(label));

    [Theory]
    [InlineData("Kirby : Nightmare", "Kirby - Nightmare")]
    [InlineData("Metal Gear Solid: The Twin Snakes", "Metal Gear Solid- The Twin Snakes")]
    [InlineData("What?", "What")]
    [InlineData("Title.", "Title")]
    [InlineData(" Spaces ", "Spaces")]
    public void Sanitize_matches_the_1_2_0_rule(string input, string expected)
        => Assert.Equal(expected, RebasePlanner.SanitizeName(input));
}
