using RomStationRebase.Models;
using RomStationRebase.ViewModels;
using Xunit;

namespace RomStationRebase.Tests;

/// <summary>
/// Ce qu'affiche une ligne du tableau de rebase : chaque interrupteur montre ce qui arrivera réellement au jeu.
/// Une règle sans objet pour lui est grisée ET éteinte, jamais « allumée mais grisée ».
/// </summary>
public class RebaseRowRulesTests
{
    private static RebaseGamePlan Plan(bool discSet = false, bool archive = true, bool converted = false, string[]? inputs = null) => new()
    {
        GameId = 1, TargetFolder = "dreamcast", IsDiscSet = discSet, HasArchive = archive, ToolInputExtensions = inputs ?? [],
        Files = [new RebaseFilePlan { Kind = converted ? FileTransferKind.Transform : FileTransferKind.Extract }],
    };

    private static RebaseGameItemViewModel Row(RebaseGamePlan plan, int files = 1) => new() { GameId = 1, FileCount = files, Plan = plan };

    private static readonly ToolOption Chd = new("chd", "chdman : CD vers CHD");

    [Fact]
    public void M3U_of_a_single_disc_game_is_off_and_locked_even_when_the_system_reads_playlists()
    {
        var row = Row(Plan(discSet: false));
        row.SetArchitectureRules(isMapped: true, keepFileName: false, m3u: true, extract: true);

        Assert.False(row.M3U);          // Bangai-O sur Dreamcast : un seul disque, pas de playlist
        Assert.False(row.CanToggleM3U);
        Assert.True(row.Extract);
        Assert.True(row.CanToggleExtract);
    }

    [Fact]
    public void M3U_of_a_real_disc_set_follows_the_architecture_and_can_be_switched_for_this_game()
    {
        var row = Row(Plan(discSet: true), files: 3);
        row.SetArchitectureRules(true, false, m3u: true, extract: true);
        int replans = 0;
        row.RuleChanged = () => replans++;

        Assert.True(row.M3U);
        Assert.True(row.CanToggleM3U);

        row.M3U = false;
        Assert.False(row.M3UOverride);
        row.M3U = true;                 // retour à la valeur de l'architecture : la surcharge s'efface
        Assert.Null(row.M3UOverride);
        Assert.Equal(2, replans);
    }

    [Fact]
    public void Several_versions_of_a_game_are_not_discs()
    {
        var row = Row(Plan(discSet: false), files: 2); // NTSC et PAL : deux fichiers, aucun M3U
        row.SetArchitectureRules(true, false, m3u: true, extract: false);

        Assert.False(row.M3U);
        Assert.False(row.CanToggleM3U);
    }

    [Fact]
    public void Romset_shows_everything_off_and_locked()
    {
        var row = Row(Plan(discSet: true, inputs: []), files: 2);
        row.SetArchitectureRules(true, keepFileName: true, m3u: true, extract: true);
        row.SetConversion("chd", [ToolOption.None], enabled: true);

        Assert.True(row.KeepFileName);
        Assert.False(row.M3U);
        Assert.False(row.Extract);
        Assert.False(row.CanToggleM3U);
        Assert.False(row.CanToggleExtract);
        Assert.Equal(string.Empty, row.Transform);
        Assert.False(row.CanChooseTransform);
    }

    [Theory]
    [InlineData(ArchiveMode.Copy, false)]        // « Ne jamais extraire » : éteint partout
    [InlineData(ArchiveMode.ExtractAll, true)]   // « Tout extraire » : allumé partout
    public void Extraction_decided_by_the_archives_mode_is_locked_and_shows_the_outcome(ArchiveMode mode, bool expected)
    {
        var row = Row(Plan());
        row.SetArchitectureRules(true, false, m3u: false, extract: !expected, mode);

        Assert.Equal(expected, row.Extract);
        Assert.False(row.CanToggleExtract);
    }

    [Fact]
    public void Game_that_is_not_an_archive_has_nothing_to_extract()
    {
        var row = Row(Plan(archive: false));
        row.SetArchitectureRules(true, false, m3u: false, extract: true, ArchiveMode.ExtractAll);

        Assert.False(row.Extract);
        Assert.False(row.CanToggleExtract);
    }

    [Fact]
    public void Conversion_takes_over_the_extraction()
    {
        var row = Row(Plan(converted: true, inputs: [".gdi"]));
        row.SetArchitectureRules(true, false, m3u: true, extract: false);
        row.SetConversion("chd", [ToolOption.None, Chd], enabled: true);

        Assert.Equal("chd", row.Transform);
        Assert.True(row.Extract);            // l'archive est bien extraite, dans le dossier de travail
        Assert.False(row.CanToggleExtract);  // mais ce n'est plus un choix : la conversion s'en charge
    }

    [Fact]
    public void Tool_is_chosen_game_by_game_and_the_architecture_only_pre_fills_it()
    {
        var row = Row(Plan(inputs: [".gdi"]));
        row.SetArchitectureRules(true, false, false, true);
        row.SetConversion(string.Empty, [ToolOption.None, Chd], enabled: true); // l'architecture ne désigne aucun outil
        int replans = 0;
        row.RuleChanged = () => replans++;

        Assert.Equal(string.Empty, row.Transform);
        Assert.True(row.CanChooseTransform);

        row.Transform = "chd";
        Assert.Equal("chd", row.TransformOverride);
        row.Transform = string.Empty;        // retour à la valeur de l'architecture
        Assert.Null(row.TransformOverride);
        Assert.Equal(2, replans);

        row.Transform = null!;               // une ComboBox dont la liste change écrit null : ce n'est pas un choix
        Assert.Null(row.TransformOverride);
        Assert.Equal(2, replans);
    }

    [Fact]
    public void Tool_the_game_cannot_use_falls_back_to_none()
    {
        var row = Row(Plan(inputs: [".cdi"]));
        row.SetArchitectureRules(true, false, false, true);

        // L'architecture désigne chdman, mais aucun outil disponible ne lit ce jeu (exécutable manquant, ou contenu .cdi) :
        // la liste reste ouverte, avec « Aucune » pour seul choix
        row.SetConversion("chd", [ToolOption.None], enabled: true);
        Assert.Equal(string.Empty, row.Transform);
        Assert.True(row.CanChooseTransform);
        Assert.Equal([ToolOption.None], row.ToolOptions);

        // Interrupteur général éteint : plus aucun choix, tout retombe sur « Aucune »
        row.SetConversion("chd", [ToolOption.None, Chd], enabled: false);
        Assert.Equal(string.Empty, row.Transform);
        Assert.False(row.CanChooseTransform);
    }
}
