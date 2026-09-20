using System.IO;
using RomStationRebase.Models;
using RomStationRebase.Services;
using Xunit;

namespace RomStationRebase.Tests;

/// <summary>Sauvegarde optionnelle d'un gamelist.xml valide avant fusion (option utilisateur 1.3.0).</summary>
public class GamelistBackupTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rsr-tests", Guid.NewGuid().ToString("N"));

    public GamelistBackupTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static GamelistGame Game(string path, string name)
        => new(path, name, null, null, null, null, null, null, null);

    [Fact]
    public void Valid_existing_file_is_copied_as_gamelist_xml_yyyymmdd_before_merge()
    {
        string path = Path.Combine(_dir, "gamelist.xml");
        GamelistService.WriteOrMerge(path, [Game("./a.zip", "A")]);
        string before = File.ReadAllText(path);

        var now    = new DateTime(2026, 9, 13, 18, 30, 0);
        var result = GamelistService.WriteOrMerge(path, [Game("./b.zip", "B")], now, backupExisting: true);

        Assert.True(result.Merged);
        Assert.Equal(Path.Combine(_dir, "gamelist.xml.20260913"), result.UserBackupPath);
        Assert.Equal(before, File.ReadAllText(result.UserBackupPath!));
        Assert.Contains("./b.zip", File.ReadAllText(path));
    }

    [Fact]
    public void Backup_is_never_overwritten_on_the_same_day()
    {
        string path = Path.Combine(_dir, "gamelist.xml");
        GamelistService.WriteOrMerge(path, [Game("./a.zip", "A")]);
        var now = new DateTime(2026, 9, 13, 18, 30, 0);

        var first  = GamelistService.WriteOrMerge(path, [Game("./b.zip", "B")], now, backupExisting: true);
        var second = GamelistService.WriteOrMerge(path, [Game("./c.zip", "C")], now, backupExisting: true);
        var third  = GamelistService.WriteOrMerge(path, [Game("./d.zip", "D")], now, backupExisting: true);

        Assert.Equal(Path.Combine(_dir, "gamelist.xml.20260913"),          first.UserBackupPath);
        Assert.Equal(Path.Combine(_dir, "gamelist.xml.20260913-183000"),   second.UserBackupPath);
        Assert.Equal(Path.Combine(_dir, "gamelist.xml.20260913-183000-2"), third.UserBackupPath);
        Assert.True(File.Exists(first.UserBackupPath!));
        Assert.True(File.Exists(second.UserBackupPath!));
        Assert.True(File.Exists(third.UserBackupPath!));
    }

    [Fact]
    public void No_backup_when_the_option_is_off_or_the_file_is_new()
    {
        string path = Path.Combine(_dir, "gamelist.xml");
        var created = GamelistService.WriteOrMerge(path, [Game("./a.zip", "A")], backupExisting: true);
        Assert.Null(created.UserBackupPath);

        var merged = GamelistService.WriteOrMerge(path, [Game("./b.zip", "B")], backupExisting: false);
        Assert.Null(merged.UserBackupPath);
        Assert.Single(Directory.GetFiles(_dir));
    }
}
