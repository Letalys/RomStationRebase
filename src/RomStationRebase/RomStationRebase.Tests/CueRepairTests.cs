using System.IO;
using System.Text;
using RomStationRebase.Services;
using Xunit;

namespace RomStationRebase.Tests;

/// <summary>
/// Réparation des .cue de RomStation qui citent un .bin renommé depuis. Relevé le 2026-09-19 sur une carte réelle :
/// 29 .cue Playstation sur 59, par exemple « ALUNDRA_PAL.BIN » cité pour un fichier « Alundra.bin ».
/// </summary>
public class CueRepairTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rsr-cue-" + Guid.NewGuid().ToString("N"));

    public CueRepairTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { /* sans importance */ } }

    private string Cue(string content, Encoding? encoding = null)
    {
        string path = Path.Combine(_dir, "Alundra.cue");
        File.WriteAllText(path, content, encoding ?? new UTF8Encoding(false));
        return path;
    }

    private void Image(string name) => File.WriteAllText(Path.Combine(_dir, name), "x");

    [Fact]
    public void Cue_citing_a_renamed_bin_is_pointed_to_the_only_image_present()
    {
        Image("Alundra.bin");
        string cue = Cue("FILE \"ALUNDRA_PAL.BIN\" BINARY\r\n  TRACK 01 MODE2/2352\r\n    INDEX 01 00:00:00\r\n");

        Assert.True(CueSheetService.RepairFileReference(cue));
        Assert.Equal("FILE \"Alundra.bin\" BINARY\r\n  TRACK 01 MODE2/2352\r\n    INDEX 01 00:00:00\r\n", File.ReadAllText(cue));
    }

    [Fact]
    public void Correct_cue_is_left_untouched_whatever_the_case_of_the_name()
    {
        Image("Alundra.bin");
        string content = "FILE \"ALUNDRA.BIN\" BINARY\r\n  TRACK 01 MODE2/2352\r\n";
        string cue = Cue(content);

        Assert.False(CueSheetService.RepairFileReference(cue)); // Windows ne distingue pas la casse : le fichier existe
        Assert.Equal(content, File.ReadAllText(cue));
    }

    [Fact]
    public void Ambiguous_cases_are_never_guessed()
    {
        // Deux images dans le dossier : laquelle est la bonne ?
        Image("a.bin"); Image("b.bin");
        string cue = Cue("FILE \"absent.bin\" BINARY\r\n  TRACK 01 MODE2/2352\r\n");
        Assert.False(CueSheetService.RepairFileReference(cue));

        // Un .cue à plusieurs fichiers, une piste par fichier : jamais retouché
        File.Delete(Path.Combine(_dir, "b.bin"));
        cue = Cue("FILE \"t1.bin\" BINARY\r\n  TRACK 01 MODE2/2352\r\nFILE \"t2.bin\" BINARY\r\n  TRACK 02 AUDIO\r\n");
        Assert.False(CueSheetService.RepairFileReference(cue));
    }

    [Fact]
    public void Accented_names_and_foreign_encodings_survive_the_repair()
    {
        Image("Légende.bin");
        // .cue en Windows-1252, avec une remarque accentuée qui doit rester telle quelle, octet pour octet
        var latin = Encoding.Latin1;
        string cue = Cue("REM Édition française\r\nFILE \"VIEUX-NOM.BIN\" BINARY\r\n  TRACK 01 MODE2/2352\r\n", latin);

        Assert.True(CueSheetService.RepairFileReference(cue));
        byte[] bytes = File.ReadAllBytes(cue);
        Assert.Equal(0xC9, bytes[4]);                                           // le « É » d'origine, toujours sur un octet
        Assert.Contains("FILE \"Légende.bin\" BINARY", Encoding.UTF8.GetString(bytes)); // le nouveau nom, lisible en UTF-8
        Assert.True(File.Exists(Path.Combine(_dir, "Légende.bin")));
    }
}
