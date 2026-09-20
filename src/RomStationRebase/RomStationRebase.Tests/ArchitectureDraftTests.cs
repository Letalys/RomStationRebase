using System.IO;
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

    [Fact]
    public void Default_architectures_never_pair_a_playlist_with_zipped_discs()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "config", "architectures");
        var broken = new List<string>();
        foreach (string file in Directory.GetFiles(dir, "*_default.json"))
        {
            var mapping = System.Text.Json.JsonSerializer.Deserialize<FolderTreeMapping>(File.ReadAllText(file),
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            foreach (var m in mapping.FolderTreeMappings)
            {
                // Un M3U qui liste des .zip ne se charge sur aucun émulateur : M3U implique extraction
                if (m.M3U && !m.Extract && !m.KeepFileName)
                    broken.Add($"{Path.GetFileName(file)} : {m.RomStationSystem}");
                // Un CUE et son BIN dans un zip ne se chargent pas : la Playstation est toujours extraite
                if (m.RomStationSystem == "Playstation" && !m.Extract)
                    broken.Add($"{Path.GetFileName(file)} : Playstation non extraite");
            }
        }
        Assert.True(broken.Count == 0, string.Join("\n", broken));
    }
}
