using System.IO;
using System.IO.Compression;
using RomStationRebase.Services;
using Xunit;

namespace RomStationRebase.Tests;

/// <summary>Extraction de zips réels fabriqués dans un dossier temporaire : arborescence, renommage, sécurité, annulation, nettoyage.</summary>
public class ArchiveExtractorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "RsrTests", Guid.NewGuid().ToString("N"));

    public ArchiveExtractorTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* meilleur effort */ }
        GC.SuppressFinalize(this);
    }

    /// <summary>Somme des octets rapportés, avec un hook optionnel appelé à chaque rapport.</summary>
    private sealed class Recorder : IProgress<long>
    {
        private readonly Action<long>? _onReport;
        public long Total { get; private set; }
        public int Reports { get; private set; }
        public Recorder(Action<long>? onReport = null) => _onReport = onReport;
        public void Report(long value)
        {
            Total += value;
            Reports++;
            _onReport?.Invoke(value);
        }
    }

    private static byte[] Bytes(int length, int seed)
    {
        var data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }

    private string CreateZip(string name, params (string entry, byte[] content)[] entries)
    {
        string path = Path.Combine(_root, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entry, content) in entries)
        {
            using var stream = zip.CreateEntry(entry).Open();
            stream.Write(content);
        }
        return path;
    }

    private string Dest(string name) => Path.Combine(_root, name);

    private static bool IsAbsentOrEmpty(string dir)
        => !Directory.Exists(dir) || !Directory.EnumerateFileSystemEntries(dir).Any();

    // ── Extraction nominale ───────────────────────────────────────────────

    [Fact]
    public async Task Multi_entry_archive_keeps_its_tree_and_reports_uncompressed_bytes()
    {
        byte[] a = Bytes(1500, 1), b = Bytes(3000, 2);
        string zip  = CreateZip("multi.zip", ("a.bin", a), ("sub/b.bin", b));
        string dest = Dest("out-multi");
        var progress = new Recorder();

        await ArchiveExtractor.ExtractAsync(zip, dest, null, progress, CancellationToken.None);

        Assert.Equal(a, await File.ReadAllBytesAsync(Path.Combine(dest, "a.bin")));
        Assert.Equal(b, await File.ReadAllBytesAsync(Path.Combine(dest, "sub", "b.bin")));
        Assert.Equal((long)(a.Length + b.Length), progress.Total);
        Assert.Equal(2, Directory.EnumerateFileSystemEntries(dest).Count());
    }

    [Fact]
    public async Task Single_entry_archive_is_written_under_the_target_name()
    {
        byte[] iso = Bytes(2048, 3);
        string zip  = CreateZip("single.zip", ("ULES00125.iso", iso));
        string dest = Dest("out-single");

        await ArchiveExtractor.ExtractAsync(zip, dest, "Burnout Legends.iso", new Recorder(), CancellationToken.None);

        Assert.Equal(iso, await File.ReadAllBytesAsync(Path.Combine(dest, "Burnout Legends.iso")));
        Assert.False(File.Exists(Path.Combine(dest, "ULES00125.iso")));
        Assert.Single(Directory.EnumerateFileSystemEntries(dest));
    }

    [Fact]
    public async Task Existing_target_file_is_overwritten()
    {
        byte[] fresh = Bytes(700, 4);
        string zip  = CreateZip("overwrite.zip", ("a.bin", fresh));
        string dest = Dest("out-overwrite");
        Directory.CreateDirectory(dest);
        string target = Path.Combine(dest, "a.bin");
        await File.WriteAllBytesAsync(target, Bytes(5000, 5));

        await ArchiveExtractor.ExtractAsync(zip, dest, null, new Recorder(), CancellationToken.None);

        Assert.Equal(fresh, await File.ReadAllBytesAsync(target));
    }

    // ── Refus ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Target_name_on_a_multi_entry_archive_is_rejected()
    {
        string zip  = CreateZip("two.zip", ("a.bin", Bytes(10, 6)), ("b.bin", Bytes(10, 7)));
        string dest = Dest("out-two");

        await Assert.ThrowsAsync<InvalidDataException>(
            () => ArchiveExtractor.ExtractAsync(zip, dest, "renamed.bin", new Recorder(), CancellationToken.None));

        Assert.True(IsAbsentOrEmpty(dest));
    }

    [Fact]
    public async Task Entry_escaping_the_destination_is_rejected_before_any_write()
    {
        // "ok.txt" est déclaré en premier : sans vérification préalable, il serait écrit avant le refus.
        string zip  = CreateZip("evil.zip", ("ok.txt", Bytes(10, 8)), ("../evil.txt", Bytes(10, 9)));
        string dest = Dest(Path.Combine("out-evil", "inner"));

        var ex = await Assert.ThrowsAsync<UnsafeArchiveException>(
            () => ArchiveExtractor.ExtractAsync(zip, dest, null, new Recorder(), CancellationToken.None));

        Assert.Equal("../evil.txt", ex.Entry);
        Assert.True(IsAbsentOrEmpty(dest));
        Assert.False(File.Exists(Path.Combine(_root, "out-evil", "evil.txt")));
        Assert.False(File.Exists(Path.Combine(_root, "evil.txt")));
    }

    // ── Annulation et nettoyage ───────────────────────────────────────────

    [Fact]
    public async Task Already_cancelled_token_extracts_nothing()
    {
        string zip  = CreateZip("cancel-early.zip", ("a.bin", Bytes(100, 10)));
        string dest = Dest("out-cancel-early");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ArchiveExtractor.ExtractAsync(zip, dest, null, new Recorder(), cts.Token));

        Assert.True(IsAbsentOrEmpty(dest));
    }

    [Fact]
    public async Task Cancellation_during_extraction_removes_partial_files_and_created_folders()
    {
        string zip  = CreateZip("cancel-mid.zip", ("sub/a.bin", Bytes(4000, 11)), ("sub/b.bin", Bytes(4000, 12)));
        string dest = Dest("out-cancel-mid");
        using var cts = new CancellationTokenSource();
        var progress = new Recorder(_ => cts.Cancel());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ArchiveExtractor.ExtractAsync(zip, dest, null, progress, cts.Token));

        Assert.True(progress.Reports >= 1);
        Assert.False(File.Exists(Path.Combine(dest, "sub", "a.bin")));
        Assert.False(File.Exists(Path.Combine(dest, "sub", "b.bin")));
        Assert.True(IsAbsentOrEmpty(dest));
    }
}
