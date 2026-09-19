using System.IO;
using RomStationRebase.Models;
using RomStationRebase.Services;
using RomStationRebase.ViewModels;
using Xunit;

namespace RomStationRebase.Tests;

/// <summary>
/// Pilotage d'un outil externe de bout en bout, sur un faux outil : un script PowerShell qui se comporte comme
/// chdman (progression sur stderr réécrite au retour chariot, fichier de sortie écrit petit à petit).
/// </summary>
public class ExternalToolTests : IDisposable
{
    private static readonly string PowerShell = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rsr-tool-" + Guid.NewGuid().ToString("N"));

    public ExternalToolTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* fichiers encore tenus par un processus en fin de vie */ }
    }

    /// <summary>Descripteur d'un faux outil dont le corps est le script donné ($in et $out y désignent l'entrée et la sortie).</summary>
    private ExternalTool FakeTool(string body, int stallSeconds = 30)
    {
        string script = Path.Combine(_dir, "tool-" + Guid.NewGuid().ToString("N") + ".ps1");
        File.WriteAllText(script, "param([string]$in, [string]$out)\r\n" + body);
        return new ExternalTool
        {
            Id = "fake", Label = "Fake", Executable = "powershell.exe",
            InputExtensions = [".gdi"], OutputExtension = ".chd",
            Arguments = ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "{input}", "{output}"],
            ProgressStream = "stderr",
            ProgressRegex  = @"(\d+(?:\.\d+)?)%",
            StallTimeoutSeconds = stallSeconds,
        };
    }

    private string Input()
    {
        string path = Path.Combine(_dir, "Game Name (Disc 1).gdi");
        File.WriteAllText(path, "3 tracks");
        return path;
    }

    private sealed class Collect : IProgress<double>
    {
        public readonly List<double> Values = new();
        public void Report(double value) { lock (Values) Values.Add(value); }
    }

    [Fact]
    public async Task Conversion_reports_carriage_return_progress_and_produces_the_output()
    {
        var tool = FakeTool("""
            [Console]::Error.Write("Compressing, 25.0% complete...`r")
            [Console]::Error.Write("Compressing, 75.5% complete...`r")
            Copy-Item -LiteralPath $in -Destination $out
            [Console]::Error.Write("Compression complete ... final ratio = 50.0%`n")
            """);
        string output = Path.Combine(_dir, "out", "Game Name (Disc 1).chd");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var progress = new Collect();

        await ExternalToolRunner.ConvertAsync(tool, PowerShell, Input(), output, progress, CancellationToken.None);

        Assert.True(File.Exists(output));
        // Les deux pourcentages séparés par un simple retour chariot ont été vus séparément, dans l'ordre
        Assert.Contains(25.0, progress.Values);
        Assert.Contains(75.5, progress.Values);
        Assert.True(progress.Values.IndexOf(25.0) < progress.Values.IndexOf(75.5));
    }

    [Fact]
    public async Task Unexpected_exit_code_fails_and_removes_the_partial_output()
    {
        var tool = FakeTool("""
            Set-Content -LiteralPath $out -Value "partial"
            [Console]::Error.WriteLine("Error: unsupported track type")
            exit 3
            """);
        string output = Path.Combine(_dir, "bad.chd");

        var ex = await Assert.ThrowsAsync<ExternalToolException>(
            () => ExternalToolRunner.ConvertAsync(tool, PowerShell, Input(), output, null, CancellationToken.None));

        Assert.Contains("3", ex.Message);
        Assert.Contains("unsupported track type", ex.Message); // les dernières lignes de l'outil accompagnent l'erreur
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task Success_exit_code_without_output_file_is_a_failure()
    {
        var tool = FakeTool("exit 0");
        string output = Path.Combine(_dir, "none.chd");

        await Assert.ThrowsAsync<ExternalToolException>(
            () => ExternalToolRunner.ConvertAsync(tool, PowerShell, Input(), output, null, CancellationToken.None));
    }

    [Fact]
    public async Task Cancellation_kills_the_tool_and_removes_the_partial_output()
    {
        var tool = FakeTool("""
            Set-Content -LiteralPath $out -Value "partial"
            [Console]::Error.Write("Compressing, 10.0% complete...`r")
            Start-Sleep -Seconds 120
            """);
        string output = Path.Combine(_dir, "cancel.chd");
        using var cts = new CancellationTokenSource();

        var started = DateTime.UtcNow;
        var task = ExternalToolRunner.ConvertAsync(tool, PowerShell, Input(), output, null, cts.Token);

        // Annuler une fois le fichier partiel écrit : c'est le cas que chdman ne nettoie pas lui-même
        while (!File.Exists(output) && DateTime.UtcNow - started < TimeSpan.FromSeconds(30)) await Task.Delay(100);
        Assert.True(File.Exists(output));
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.False(File.Exists(output));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(60)); // l'outil a été tué, pas attendu
    }

    [Fact]
    public async Task Silent_tool_is_stopped_by_the_stall_watchdog()
    {
        var tool = FakeTool("Start-Sleep -Seconds 120", stallSeconds: 2);
        string output = Path.Combine(_dir, "stall.chd");

        var started = DateTime.UtcNow;
        await Assert.ThrowsAsync<ExternalToolException>(
            () => ExternalToolRunner.ConvertAsync(tool, PowerShell, Input(), output, null, CancellationToken.None));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(60));
    }

    [Fact]
    public async Task Missing_executable_is_reported_without_throwing_anything_else()
    {
        var tool = FakeTool("exit 0");
        await Assert.ThrowsAsync<ExternalToolException>(
            () => ExternalToolRunner.ConvertAsync(tool, Path.Combine(_dir, "absent.exe"), Input(), Path.Combine(_dir, "x.chd"), null, CancellationToken.None));
    }

    [Fact]
    public void Arguments_are_expanded_one_by_one_and_never_split()
    {
        string input  = @"C:\work\in\Game & Co (Disc 1).gdi";
        string output = @"C:\work\out\Game & Co (Disc 1).chd";

        Assert.Equal(input,                  ExternalToolRunner.Expand("{input}", input, output, 8));
        Assert.Equal(@"C:\work\in",          ExternalToolRunner.Expand("{inputDir}", input, output, 8));
        Assert.Equal(@"C:\work\out",         ExternalToolRunner.Expand("{outputDir}", input, output, 8));
        Assert.Equal("Game & Co (Disc 1)",   ExternalToolRunner.Expand("{base}", input, output, 8));
        Assert.Equal("--threads=8",          ExternalToolRunner.Expand("--threads={threads}", input, output, 8));
        Assert.Equal("{unknown}",            ExternalToolRunner.Expand("{unknown}", input, output, 8));
    }

    [Fact]
    public async Task Test_button_reads_the_version_and_compares_it_to_the_minimum()
    {
        string script = Path.Combine(_dir, "version.ps1");
        File.WriteAllText(script, "Write-Output 'chdman - MAME Compressed Hunks of Data (CHD) manager 0.228 (mame0228)'");
        var tool = new ExternalTool
        {
            Executable = "powershell.exe",
            VersionArguments = ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script],
            VersionRegex = @"manager\s+(\d+\.\d+)",
            MinVersion = "0.230",
        };

        var tooOld = await ExternalToolService.TestAsync(tool, PowerShell);
        Assert.False(tooOld.Success);
        Assert.Equal("0.228", tooOld.Version);

        tool.MinVersion = "0.200";
        var fine = await ExternalToolService.TestAsync(tool, PowerShell);
        Assert.True(fine.Success);
    }

    // ── Rebase complet avec conversion ────────────────────────────────────

    private sealed class Sink : IProgress<RebaseProgress>
    {
        public RebaseProgress? Last;
        public void Report(RebaseProgress value) => Last = value;
    }

    /// <summary>Archive GDI → dossier de travail → outil → seul le résultat sur la cible, dossier de travail vidé.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Rebase_converts_in_the_work_folder_and_only_the_result_reaches_the_target(bool toolSucceeds)
    {
        string zip = Path.Combine(_dir, "ct.zip");
        using (var archive = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
        {
            foreach (var (name, text) in new[] { ("disc.gdi", "1 track03.bin"), ("track03.bin", new string('x', 5000)) })
            {
                using var w = new StreamWriter(archive.CreateEntry(name).Open());
                w.Write(text);
            }
        }

        // Le faux outil exige, comme chdman, que les pistes soient à côté du .gdi
        var tool = FakeTool(toolSucceeds
            ? """
              if (-not (Test-Path -LiteralPath (Join-Path (Split-Path -Parent $in) 'track03.bin'))) { exit 9 }
              [Console]::Error.Write("Compressing, 50.0% complete...`r")
              Set-Content -LiteralPath $out -Value "CHD"
              """
            : """
              Set-Content -LiteralPath $out -Value "partial"
              exit 1
              """);

        string target = Path.Combine(_dir, "target"), work = Path.Combine(_dir, "work");
        var info = new ArchiveInspector().Inspect(zip);
        var plan = new RebasePlan
        {
            Games =
            [
                new RebaseGamePlan
                {
                    GameId = 1, Title = "Crazy Taxi", SystemName = "Dreamcast", TargetFolder = "dreamcast",
                    BaseName = "Crazy Taxi", OutputKind = GameOutputKind.Converted, PlannedBytes = 3000,
                    Files =
                    [
                        new RebaseFilePlan
                        {
                            SourcePath = zip, Label = "Crazy Taxi", Kind = FileTransferKind.Transform,
                            LaunchRelativePath = "Crazy Taxi.chd", TransformToolId = "fake", TransformInputEntry = "disc.gdi",
                            Archive = info, CopySize = new FileInfo(zip).Length, ExtractedSize = info.UncompressedSize,
                            TransformedSizeEstimate = 3000,
                        },
                    ],
                },
            ],
        };
        var options = new RebaseOptions
        {
            Plan = plan, TargetPath = target, Architecture = new ArchitectureEntry { Id = "t" },
            RetryCount = 2, RetryDelaySeconds = 0, WorkDirectory = work,
            Tools = new Dictionary<string, (ExternalTool, string)> { ["fake"] = (tool, PowerShell) },
        };
        var sink = new Sink();

        await new RebaseService().RunRebaseAsync(options, sink, CancellationToken.None);

        string systemDir = Path.Combine(target, "dreamcast");
        var produced = Directory.Exists(systemDir) ? Directory.GetFileSystemEntries(systemDir).Select(p => Path.GetFileName(p)!).ToArray() : [];
        if (toolSucceeds)
        {
            Assert.Equal(["Crazy Taxi.chd"], produced);   // ni .gdi, ni pistes, ni sous-dossier
            Assert.Equal(0, sink.Last!.FailedFiles);
        }
        else
        {
            Assert.Empty(produced);                        // rien de tronqué sur la cible
            Assert.Equal(1, sink.Last!.FailedFiles);
        }
        Assert.False(Directory.Exists(work) && Directory.EnumerateFileSystemEntries(work).Any()); // dossier de travail vidé
    }

    // ── Service et brouillon ──────────────────────────────────────────────

    [Theory]
    [InlineData("0.230", "0.255", -1)]
    [InlineData("0.255", "0.230", 1)]
    [InlineData("1.12.0", "1.9", 1)]      // numérique, pas alphabétique
    [InlineData("1.13", "1.13.0", 0)]
    public void Versions_are_compared_segment_by_segment(string a, string b, int expected)
        => Assert.Equal(expected, Math.Sign(ExternalToolService.CompareVersions(a, b)));

    [Fact]
    public void Tool_id_is_a_safe_unique_file_name()
    {
        Assert.Equal("chdman-dvd", ExternalToolService.MakeId("chdman : DVD !", []));
        Assert.Equal("chdman-dvd-2", ExternalToolService.MakeId("chdman DVD", ["chdman-dvd"]));
        Assert.Equal("outil", ExternalToolService.MakeId("???", []));
    }

    [Fact]
    public void Executable_location_belongs_to_each_tool_and_is_only_user_chosen()
    {
        string exe = Path.Combine(_dir, "chdman.exe");
        File.WriteAllText(exe, "x");
        var cd  = new ExternalTool { Id = "cd",  Executable = "chdman.exe" };
        var dvd = new ExternalTool { Id = "dvd", Executable = "chdman.exe" };
        var cso = new ExternalTool { Id = "cso", Executable = "maxcso.exe" };
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["cd"]  = exe,
            ["cso"] = Path.Combine(_dir, "gone", "maxcso.exe"),
        };

        Assert.Equal(exe, ExternalToolService.ResolveExecutable(cd, paths));
        Assert.Null(ExternalToolService.ResolveExecutable(dvd, paths)); // même programme, mais cet outil-là n'a pas été réglé
        Assert.Null(ExternalToolService.ResolveExecutable(cso, paths)); // chemin indiqué mais fichier disparu
    }

    /// <summary>Arborescence des émulateurs de RomStation : app\emulators\downloads\&lt;Emulateur&gt;iles\&lt;version&gt;\.</summary>
    private string EmulatorFile(string emulator, string version, string file, DateTime written)
    {
        string dir = Path.Combine(_dir, "RomStation", "app", "emulators", "downloads", emulator, "files", version);
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, file);
        File.WriteAllText(path, "x");
        File.SetLastWriteTimeUtc(path, written);
        return path;
    }

    [Fact]
    public void Tool_shipped_with_a_RomStation_emulator_is_proposed_newest_first()
    {
        string rs = Path.Combine(_dir, "RomStation");
        EmulatorFile("MAME", "MAME 0.250 (x64)", "chdman.exe", new DateTime(2023, 1, 1));
        string newest = EmulatorFile("MAME", "MAME 0.271 (x64)", "chdman.exe", new DateTime(2024, 11, 1));
        EmulatorFile("Dolphin", "Dolphin 2412 (x64)", "Dolphin.exe", new DateTime(2024, 12, 1));

        Assert.Equal(newest, ExternalToolService.DetectInRomStation(new ExternalTool { Executable = "chdman.exe" }, rs));
        Assert.Equal(newest, ExternalToolService.Detect(new ExternalTool { Executable = "chdman.exe" }, rs)); // passe avant le PATH
        Assert.Null(ExternalToolService.DetectInRomStation(new ExternalTool { Executable = "maxcso.exe" }, rs));
        Assert.Null(ExternalToolService.DetectInRomStation(new ExternalTool { Executable = "chdman.exe" }, Path.Combine(_dir, "absent")));
        Assert.True(ExternalToolService.IsRomStationEmulatorPath(newest));
    }

    [Fact]
    public void Emulator_folders_recorded_by_RomStation_take_precedence_over_the_disk_layout()
    {
        string rs = Path.Combine(_dir, "RomStation");
        // Sur le disque : un vieux MAME que RomStation ne connaît plus, et le MAME qu'il a enregistré
        EmulatorFile("MAME", "MAME 0.999 (orphelin)", "chdman.exe", new DateTime(2030, 1, 1));
        string recorded = EmulatorFile("MAME", "MAME 0.271 (x64)", "chdman.exe", new DateTime(2024, 11, 1));
        // Un émulateur ajouté à la main, hors du dossier de RomStation : chemin absolu dans la base
        string manualDir = Path.Combine(_dir, "MonDolphin");
        Directory.CreateDirectory(manualDir);
        File.WriteAllText(Path.Combine(manualDir, "DolphinTool.exe"), "x");

        string[] fromDatabase = [@"emulators\downloads\PPSSPP\files\PPSSPP v1.18.1 (x64)", @"emulators\downloads\MAME\files\MAME 0.271 (x64)", manualDir];

        Assert.Equal(recorded, ExternalToolService.DetectInRomStation(new ExternalTool { Executable = "chdman.exe" }, rs, fromDatabase));
        Assert.Equal(Path.Combine(manualDir, "DolphinTool.exe"),
                     ExternalToolService.DetectInRomStation(new ExternalTool { Executable = "DolphinTool.exe" }, rs, fromDatabase));
        Assert.Null(ExternalToolService.DetectInRomStation(new ExternalTool { Executable = "maxcso.exe" }, rs, fromDatabase));
    }

    [Fact]
    public void Chosen_location_follows_the_emulator_when_RomStation_updates_it()
    {
        string old = EmulatorFile("MAME", "MAME 0.271 (x64)", "chdman.exe", new DateTime(2024, 11, 1));
        var tool  = new ExternalTool { Id = "cd", Executable = "chdman.exe" };
        var paths = new Dictionary<string, string> { ["cd"] = old };
        Assert.Equal(old, ExternalToolService.ResolveExecutable(tool, paths));

        // RomStation remplace le dossier de version : l'ancien chemin n'existe plus
        Directory.Delete(Path.GetDirectoryName(old)!, recursive: true);
        Assert.Null(ExternalToolService.ResolveExecutable(tool, paths));
        string updated = EmulatorFile("MAME", "MAME 0.272 (x64)", "chdman.exe", new DateTime(2024, 12, 1));
        Assert.Equal(updated, ExternalToolService.ResolveExecutable(tool, paths));

        // Hors des émulateurs de RomStation, un chemin disparu reste disparu : on ne cherche nulle part ailleurs
        string own = Path.Combine(_dir, "tools", "v1", "chdman.exe");
        Directory.CreateDirectory(Path.Combine(_dir, "tools", "v2"));
        File.WriteAllText(Path.Combine(_dir, "tools", "v2", "chdman.exe"), "x");
        paths["cd"] = own;
        Assert.Null(ExternalToolService.ResolveExecutable(tool, paths));
    }

    [Fact]
    public void Default_tool_descriptors_are_valid()
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "config", "tools");
        var files = Directory.GetFiles(folder, "*.json");
        Assert.NotEmpty(files);

        foreach (string file in files)
        {
            var tool = System.Text.Json.JsonSerializer.Deserialize<ExternalTool>(File.ReadAllText(file),
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            Assert.Equal(Path.GetFileNameWithoutExtension(file), tool.Id);

            var draft = new ExternalToolDraftViewModel(tool, null, isPersisted: true);
            Assert.Empty(draft.Validate());
            Assert.False(draft.IsDirty);
        }
    }

    [Fact]
    public void Draft_turns_dirty_on_edit_and_clean_again_when_reverted()
    {
        var tool  = new ExternalTool { Id = "t", Label = "T", Executable = "t.exe", InputExtensions = [".iso"], OutputExtension = ".cso", Arguments = ["{input}", "{output}"] };
        var draft = new ExternalToolDraftViewModel(tool, null, isPersisted: true);

        draft.InputExtensionsText = "ISO, .bin";
        Assert.True(draft.IsDirty);
        Assert.Equal(".iso .bin", draft.InputExtensionsText); // point ajouté, minuscules

        draft.InputExtensionsText = ".iso";
        Assert.False(draft.IsDirty);

        draft.ExecutablePath = @"C:\tools\t.exe";
        Assert.True(draft.IsDirty);                          // l'emplacement fait partie de ce qui s'enregistre
        Assert.False(draft.IsAvailable);

        draft.ArgumentsText = "{input}";
        Assert.Contains(draft.Validate(), e => e.Contains("{output}"));
    }
}
