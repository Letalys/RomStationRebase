using RomStationRebase.Models;
using RomStationRebase.ViewModels;
using Xunit;

namespace RomStationRebase.Tests;

/// <summary>Le brouillon de l'éditeur d'architectures doit refléter chaque bascule de ligne dans le mapping écrit.</summary>
public class ArchitectureDraftTests
{
    private static ArchitectureDraftViewModel NewDraft()
    {
        var draft = new ArchitectureDraftViewModel(new ArchitectureEntry { Id = "retroarch", Label = "RetroArch", FolderTreeMapping = "retroarch.json" });
        draft.AllSystems = ["Playstation", "PSP", "Neo-Geo", "Wii U"];
        draft.LoadMappings(new FolderTreeMapping
        {
            FolderTreeMappings =
            [
                new() { RomStationSystem = "Playstation", TargetFolder = "psx", M3U = true },
                new() { RomStationSystem = "PSP",         TargetFolder = "psp", Extract = true },
            ],
        });
        return draft;
    }

    [Fact]
    public void Toggling_a_row_rule_marks_the_draft_dirty_and_is_written_to_the_mapping()
    {
        var draft = NewDraft();
        Assert.False(draft.IsDirty);

        draft.Mappings[0].KeepFileName = true;

        Assert.True(draft.IsDirty);
        var mapping = draft.ToMapping();
        Assert.True(mapping.FolderTreeMappings[0].KeepFileName);
        Assert.True(mapping.FolderTreeMappings[0].M3U);
        Assert.False(mapping.FolderTreeMappings[1].KeepFileName);
    }

    [Fact]
    public void Each_row_only_offers_systems_not_used_by_another_row()
    {
        var draft = NewDraft();

        Assert.Equal(["Playstation", "Neo-Geo", "Wii U"], draft.Mappings[0].AvailableSystems);
        Assert.Equal(["PSP", "Neo-Geo", "Wii U"],         draft.Mappings[1].AvailableSystems);

        draft.Mappings.Add(new SystemMappingRowViewModel { RomStationSystem = "Neo-Geo", TargetFolder = "neogeo" });
        Assert.Equal(["Playstation", "Wii U"], draft.Mappings[0].AvailableSystems);

        draft.Mappings[2].RomStationSystem = "Wii U";
        Assert.Equal(["Playstation", "Neo-Geo"], draft.Mappings[0].AvailableSystems);
    }

    [Fact]
    public void Refreshing_available_systems_does_not_dirty_the_draft()
    {
        var draft = NewDraft();
        draft.AllSystems = ["Playstation", "PSP"];
        Assert.False(draft.IsDirty);
    }
}
