using System.IO;
using System.Text;
using RomStationRebase.Helpers;
using RomStationRebase.Models;
using RomStationRebase.Resources;

namespace RomStationRebase.Services;

/// <summary>
/// Exécute le rebase à partir du plan calculé par RebasePlanner : copie ou extraction des fichiers,
/// M3U, jaquettes, puis fichiers de métadonnées par dossier système.
/// </summary>
public class RebaseService
{
    private const int ChunkSize = 1_048_576; // 1 Mo par chunk

    /// <summary>
    /// Lance le rebase de tous les jeux du plan.
    /// Copie en parallèle (SemaphoreSlim), respecte la pause et l'annulation,
    /// écrit les gamelists une fois toutes les copies terminées, et rapporte la progression via IProgress.
    /// </summary>
    public async Task RunRebaseAsync(
        RebaseOptions options,
        IProgress<RebaseProgress> progress,
        CancellationToken ct)
    {
        var items = options.Plan.Games.Select(p => new RebaseGameItem
        {
            GameId          = p.GameId,
            Title           = p.Title,
            SystemName      = p.SystemName,
            SystemImagePath = p.SystemImagePath,
            FileCount       = p.Files.Count,
            Plan            = p,
        }).ToList();

        // Restes d'une conversion interrompue brutalement (coupure, plantage) : le dossier de travail repart vide
        if (options.Plan.Games.Any(g => g.Files.Any(f => f.Kind == FileTransferKind.Transform)))
            CleanWorkDirectory(string.IsNullOrWhiteSpace(options.WorkDirectory) ? ExternalToolService.WorkDirectory : options.WorkDirectory);

        long totalBytes  = options.Plan.TotalBytes;
        long copiedBytes = 0;
        int  completed   = 0;
        int  failed      = 0;
        int  skipped     = 0;

        var semaphore               = new SemaphoreSlim(options.MaxParallelCopies, options.MaxParallelCopies);
        var startTime               = DateTime.UtcNow;
        double lastReportedProgress = 0.0; // throttle basé sur le delta de pourcentage

        // Rapport de progression — appelé depuis n'importe quel thread (Progress<T> marshale sur UI)
        void Report(RebaseGameItem? current = null, RebasePhase phase = RebasePhase.Transferring,
                    int metadataFolders = 0, IReadOnlyList<string>? notes = null)
        {
            var elapsed    = (DateTime.UtcNow - startTime).TotalSeconds;
            long copied    = Interlocked.Read(ref copiedBytes);
            double speed   = elapsed > 0 ? copied / elapsed : 0;
            long remaining = Math.Max(0, totalBytes - copied);
            var eta        = speed > 0 ? TimeSpan.FromSeconds(remaining / speed) : TimeSpan.Zero;

            progress.Report(new RebaseProgress
            {
                Phase                  = phase,
                TotalFiles             = items.Count,
                CompletedFiles         = completed,
                FailedFiles            = failed,
                SkippedFiles           = skipped,
                TotalBytes             = totalBytes,
                CopiedBytes            = copied,
                SpeedBytesPerSecond    = speed,
                EstimatedTimeRemaining = eta,
                CurrentItem            = current,
                MetadataFoldersWritten = metadataFolders,
                MetadataNotes          = notes ?? [],
            });
        }

        // Progression par octets, throttlée à 0.5 % — évite que les threads parallèles se bloquent
        // mutuellement avec un throttle temporel (race sur lastReport)
        IProgress<long> BytesProgress(RebaseGameItem item) => new Progress<long>(bytes =>
        {
            Interlocked.Add(ref copiedBytes, bytes);
            double newPct = totalBytes > 0
                ? (double)Interlocked.Read(ref copiedBytes) / totalBytes * 100
                : 0;
            if (newPct - lastReportedProgress >= 0.5)
            {
                lastReportedProgress = newPct;
                Report(item);
            }
        });

        // Task.Run obligatoire : les lambdas async démarrées depuis le thread UI y restent si WaitAsync
        // complète synchroniquement. PauseEvent.Wait bloquerait alors le thread UI.
        var tasks = items.Select(item => Task.Run(async () =>
        {
            await semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                options.PauseEvent.Wait(ct); // sûr : exécuté sur un thread pool
                ct.ThrowIfCancellationRequested();

                var plan = item.Plan;
                if (plan.IsUnmapped)
                {
                    // Système sans mapping → on saute le jeu
                    item.Status    = RebaseItemStatus.Skipped;
                    item.IsSkipped = true;
                    Interlocked.Increment(ref skipped);
                    Report(item);
                    return;
                }

                item.Status = RebaseItemStatus.Copying;
                Report(item);

                string destSys = Path.Combine(options.TargetPath, plan.TargetFolder!);
                Directory.CreateDirectory(destSys);

                int ignoredCount = 0;
                var warnings     = new List<string>();
                try
                {
                    foreach (var file in plan.Files)
                    {
                        ct.ThrowIfCancellationRequested();
                        options.PauseEvent.Wait(ct);

                        if (!File.Exists(file.SourcePath) && file.Kind != FileTransferKind.CopyTree)
                        {
                            item.Status      = RebaseItemStatus.Failed;
                            item.ErrorDetail = $"Source introuvable : {Path.GetFileName(file.SourcePath)}";
                            Interlocked.Increment(ref failed);
                            Report(item);
                            return;
                        }

                        string launchPath = Path.Combine(destSys, file.LaunchRelativePath.Replace('/', Path.DirectorySeparatorChar));
                        string? cueBinPath = file.CueBinRelativePath is null
                            ? null
                            : Path.Combine(destSys, file.CueBinRelativePath.Replace('/', Path.DirectorySeparatorChar));

                        // Image disque déjà extraite par un rebase précédent, mais sans son .cue : on n'écrit que lui
                        if (cueBinPath is not null && !File.Exists(launchPath) && File.Exists(cueBinPath)
                            && options.DuplicatePolicy == DuplicatePolicy.Ignore)
                        {
                            CueSheetService.WriteFor(cueBinPath, launchPath);
                            Interlocked.Add(ref copiedBytes, file.PlannedBytes);
                            continue;
                        }

                        if (File.Exists(launchPath) && options.DuplicatePolicy == DuplicatePolicy.Ignore)
                        {
                            // Déjà présent : compté comme écrit pour que la barre et l'ETA restent justes
                            ignoredCount++;
                            Interlocked.Add(ref copiedBytes, file.PlannedBytes);
                            continue;
                        }

                        item.Status = file.Kind switch
                        {
                            FileTransferKind.Extract   => RebaseItemStatus.Extracting,
                            FileTransferKind.Transform => RebaseItemStatus.Converting,
                            _                          => RebaseItemStatus.Copying,
                        };
                        Report(item);

                        var bytesProgress = BytesProgress(item);
                        if (file.Kind == FileTransferKind.Transform)
                            await RunWithRetryAsync(
                                () => TransformAsync(file, destSys, options, bytesProgress, ct),
                                file.SourcePath, options.RetryCount, options.RetryDelaySeconds, ct).ConfigureAwait(false);
                        else
                            await RunWithRetryAsync(
                                () => TransferAsync(file, destSys, bytesProgress, ct),
                                file.SourcePath, options.RetryCount, options.RetryDelaySeconds, ct).ConfigureAwait(false);

                        // Image disque brute livrée sans descripteur : le .cue qui la rend lançable
                        if (cueBinPath is not null)
                            CueSheetService.WriteFor(cueBinPath, launchPath);
                    }

                    // Playlist M3U des vrais disques, réécrite à chaque passage (idempotent, quelques octets)
                    if (plan.M3URelativePath is not null && plan.Files.Count > 0)
                    {
                        string m3uPath = Path.Combine(destSys, plan.M3URelativePath);
                        await File.WriteAllTextAsync(m3uPath,
                            GenerateM3UContent(plan.Title, plan.M3UEntries), ct).ConfigureAwait(false);
                    }

                    // Jaquettes : secondaires, une erreur ne fait pas échouer le jeu
                    foreach (var cover in plan.Covers)
                    {
                        ct.ThrowIfCancellationRequested();
                        string coverDest = Path.Combine(cover.IsRootRelative ? options.TargetPath : destSys,
                            cover.DestRelativePath.Replace('/', Path.DirectorySeparatorChar));
                        long coverBytes  = GetFileSize(cover.SourcePath);
                        if (File.Exists(coverDest) && options.DuplicatePolicy == DuplicatePolicy.Ignore)
                        {
                            Interlocked.Add(ref copiedBytes, coverBytes);
                            continue;
                        }
                        try
                        {
                            CoverService.CopyCover(cover.SourcePath, coverDest,
                                options.Architecture.CoverMaxWidth, options.Architecture.CoverMaxHeight);
                            Interlocked.Add(ref copiedBytes, coverBytes);
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex)
                        {
                            warnings.Add($"{Path.GetFileName(coverDest)} : {ErrorMessageClassifier.Classify(ex)}");
                        }
                    }

                    // Tous les fichiers ignorés par la politique Ignore → statut Skipped
                    if (ignoredCount == plan.Files.Count && plan.Files.Count > 0)
                    {
                        item.Status    = RebaseItemStatus.Skipped;
                        item.IsSkipped = true;
                        item.Progress  = 100;
                        Interlocked.Increment(ref skipped);
                    }
                    else
                    {
                        item.Status   = RebaseItemStatus.Done;
                        item.Progress = 100;
                        Interlocked.Increment(ref completed);
                    }

                    if (warnings.Count > 0)
                        item.ErrorDetail = string.Join(" · ", warnings);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    item.Status      = RebaseItemStatus.Failed;
                    item.ErrorDetail = ErrorMessageClassifier.Classify(ex);
                    Interlocked.Increment(ref failed);
                }

                Report(item);
            }
            finally
            {
                semaphore.Release();
            }
        })).ToList();

        await Task.WhenAll(tasks);

        // ── Métadonnées : un gamelist par dossier système, après la dernière copie ──
        int   foldersWritten = 0;
        var   notes          = new List<string>();
        if (options.GenerateGamelist)
        {
            Report(phase: RebasePhase.WritingMetadata);
            ct.ThrowIfCancellationRequested();
            (foldersWritten, notes) = await Task.Run(() => WriteGamelists(options, items), ct).ConfigureAwait(false);
        }

        Report(phase: RebasePhase.Completed, metadataFolders: foldersWritten, notes: notes); // rapport final agrégé
    }

    // ── Transferts ────────────────────────────────────────────────────────

    /// <summary>Exécute le transfert d'un fichier du plan selon sa nature.</summary>
    private static async Task TransferAsync(RebaseFilePlan file, string destSys, IProgress<long> bytesProgress, CancellationToken ct)
    {
        switch (file.Kind)
        {
            case FileTransferKind.Copy:
            {
                string dest = Path.Combine(destSys, file.LaunchRelativePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                await CopyFileInternalAsync(file.SourcePath, dest, bytesProgress, ct).ConfigureAwait(false);
                break;
            }
            case FileTransferKind.Extract:
            {
                string destDir = file.DestRelativeDir.Length == 0
                    ? destSys
                    : Path.Combine(destSys, file.DestRelativeDir);
                await ArchiveExtractor.ExtractAsync(file.SourcePath, destDir, file.SingleEntryTargetName,
                    bytesProgress, ct).ConfigureAwait(false);
                break;
            }
            case FileTransferKind.CopyTree:
            {
                string sourceDir = file.SourceDirectory ?? Path.GetDirectoryName(file.SourcePath) ?? string.Empty;
                string destDir   = Path.Combine(destSys, file.DestRelativeDir);
                if (!Directory.Exists(sourceDir))
                    throw new DirectoryNotFoundException($"Source introuvable : {sourceDir}");

                foreach (string src in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
                {
                    ct.ThrowIfCancellationRequested();
                    string rel  = Path.GetRelativePath(sourceDir, src);
                    string dest = Path.Combine(destDir, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    await CopyFileInternalAsync(src, dest, bytesProgress, ct).ConfigureAwait(false);
                }
                break;
            }
        }
    }

    // ── Conversion par outil externe ──────────────────────────────────────

    /// <summary>
    /// Progression rapportée sur place, dans l'ordre. Progress&lt;T&gt; poste ses rappels sur le pool de threads : ils peuvent
    /// arriver dans le désordre, voire après la fin de l'étape, ce qui fausserait une progression cumulée.
    /// </summary>
    private sealed class InlineProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    /// <summary>Vide le dossier de travail. Un seul rebase tourne à la fois (instance unique, fenêtre modale) : rien d'utile ne peut s'y trouver.</summary>
    private static void CleanWorkDirectory(string workRoot)
    {
        try
        {
            if (!Directory.Exists(workRoot)) return;
            foreach (string dir in Directory.GetDirectories(workRoot))
            {
                try { Directory.Delete(dir, recursive: true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* encore tenu : repris au prochain rebase */ }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* dossier inaccessible : la conversion le dira elle-même */ }
    }

    /// <summary>Les outils de conversion occupent déjà tous les cœurs : une conversion à la fois, pendant que les copies continuent.</summary>
    private readonly SemaphoreSlim _conversionGate = new(1, 1);

    // Part de la taille prévue créditée à chaque étape, pour une barre qui avance du début à la fin
    private const double ExtractShare = 0.15, ConvertShare = 0.75;

    /// <summary>
    /// Conversion pilotée de bout en bout : extraction dans un dossier de travail local (un .gdi a besoin de ses pistes
    /// à côté de lui), lancement de l'outil, copie du seul résultat vers la cible sous le nom du plan, nettoyage.
    /// Rien n'est écrit sur la cible avant que l'outil ait réussi.
    /// </summary>
    private async Task TransformAsync(RebaseFilePlan file, string destSys, RebaseOptions options,
                                      IProgress<long> bytesProgress, CancellationToken ct)
    {
        if (file.TransformToolId is null || !options.Tools.TryGetValue(file.TransformToolId, out var entry))
            throw new ExternalToolException(string.Format(Strings.Tools_Error_Unavailable, file.TransformToolId));

        long planned  = Math.Max(1, file.PlannedBytes);
        long credited = 0;
        object creditLock = new();
        // Progression toujours croissante et bornée à la taille prévue : l'outil écrit sur deux flux, donc deux threads
        void Credit(long upTo)
        {
            lock (creditLock)
            {
                upTo = Math.Clamp(upTo, credited, planned);
                if (upTo > credited) { bytesProgress.Report(upTo - credited); credited = upTo; }
            }
        }

        string workRoot = string.IsNullOrWhiteSpace(options.WorkDirectory) ? ExternalToolService.WorkDirectory : options.WorkDirectory;
        string workDir  = Path.Combine(workRoot, Guid.NewGuid().ToString("N"));

        await _conversionGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(workDir);

            // 1. L'entrée de l'outil : extraite de l'archive, ou la source elle-même
            string input;
            if (file.TransformInputEntry is not null)
            {
                string extractDir = Path.Combine(workDir, "in");
                long extracted = 0, total = Math.Max(1, file.ExtractedSize);
                var extractProgress = new InlineProgress<long>(b =>
                {
                    extracted += b;
                    Credit((long)(planned * ExtractShare * Math.Min(1.0, (double)extracted / total)));
                });
                await ArchiveExtractor.ExtractAsync(file.SourcePath, extractDir, null, extractProgress, ct).ConfigureAwait(false);
                input = Path.Combine(extractDir, file.TransformInputEntry.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(input))
                    throw new ExternalToolException(string.Format(Strings.Tools_Error_InputMissing, file.TransformInputEntry));
            }
            else
            {
                input = file.SourcePath;
            }
            Credit((long)(planned * ExtractShare));

            // 2. L'outil, dans le dossier de travail : jamais sur la carte de destination, bien plus lente
            string outDir = Path.Combine(workDir, "out");
            Directory.CreateDirectory(outDir);
            string output = Path.Combine(outDir, Path.GetFileName(file.LaunchRelativePath));

            var percent = new InlineProgress<double>(p =>
                Credit((long)(planned * (ExtractShare + ConvertShare * p / 100.0))));
            await ExternalToolRunner.ConvertAsync(entry.Tool, entry.ExecutablePath, input, output, percent, ct).ConfigureAwait(false);
            Credit((long)(planned * (ExtractShare + ConvertShare)));

            // 3. Le seul résultat atteint la cible, sous le nom décidé par le plan
            string dest = Path.Combine(destSys, file.LaunchRelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            long size = Math.Max(1, new FileInfo(output).Length), copied = 0;
            long copyFrom = credited;
            var copyProgress = new InlineProgress<long>(b =>
            {
                copied += b;
                Credit(copyFrom + (long)((planned - copyFrom) * Math.Min(1.0, (double)copied / size)));
            });
            try
            {
                await CopyFileInternalAsync(output, dest, copyProgress, ct).ConfigureAwait(false);
            }
            catch
            {
                ExternalToolRunner.TryDelete(dest); // jamais de fichier converti tronqué sur la cible
                throw;
            }
            Credit(planned);
        }
        finally
        {
            _conversionGate.Release();
            try { if (Directory.Exists(workDir)) Directory.Delete(workDir, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* sera repris au prochain nettoyage du dossier de travail */ }
        }
    }

    /// <summary>Relance une opération en cas d'échec, avec délai entre deux tentatives.</summary>
    internal static async Task RunWithRetryAsync(
        Func<Task> operation, string sourceForLog,
        int retryCount, int retryDelaySeconds, CancellationToken ct)
    {
        for (int attempt = 0; attempt <= retryCount; attempt++)
        {
            try
            {
                await operation().ConfigureAwait(false);
                return; // succès
            }
            // Une archive dangereuse ou corrompue ne le sera pas moins au prochain essai
            catch (Exception ex) when (attempt < retryCount && !ct.IsCancellationRequested
                                       && ex is not UnsafeArchiveException and not InvalidDataException
                                                and not ExternalToolException) // un outil qui échoue échouera pareil au second essai
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Rebase] Retry {attempt + 1}/{retryCount} for {Path.GetFileName(sourceForLog)}");
                await Task.Delay(retryDelaySeconds * 1000, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Copie effective du fichier par chunks de 1 Mo.</summary>
    private static async Task CopyFileInternalAsync(
        string source, string dest,
        IProgress<long> bytesProgress,
        CancellationToken ct)
    {
        using var input  = new FileStream(source, FileMode.Open,   FileAccess.Read,  FileShare.Read, ChunkSize, useAsync: true);
        using var output = new FileStream(dest,   FileMode.Create, FileAccess.Write, FileShare.None, ChunkSize, useAsync: true);
        var buffer = new byte[ChunkSize];
        int read;
        while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            bytesProgress.Report(read);
        }
    }

    /// <summary>
    /// Génère le contenu d'un fichier M3U pour un jeu multi-disques.
    /// Le fichier liste les chemins des disques relatifs au dossier système, un par ligne, précédés d'un commentaire titre.
    /// </summary>
    public string GenerateM3UContent(string gameTitle, IReadOnlyList<string> discPaths)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {gameTitle}");
        foreach (var file in discPaths)
            sb.AppendLine(file);
        return sb.ToString();
    }

    // ── Métadonnées ───────────────────────────────────────────────────────

    /// <summary>
    /// Écrit ou fusionne le gamelist.xml de chaque dossier système ayant reçu au moins un jeu
    /// (copié maintenant ou déjà présent). Retourne le nombre de dossiers écrits et les remarques à afficher.
    /// </summary>
    private static (int, List<string>) WriteGamelists(RebaseOptions options, List<RebaseGameItem> items)
    {
        var notes = new List<string>();
        int written = 0;

        var byFolder = items
            .Where(i => i.Status is RebaseItemStatus.Done or RebaseItemStatus.Skipped
                        && !i.Plan.IsUnmapped && i.Plan.GamelistEntries.Count > 0)
            .GroupBy(i => i.Plan.TargetFolder!, StringComparer.OrdinalIgnoreCase);

        string format = options.Architecture.GamelistFormat ?? string.Empty;
        if (!MetadataFormats.IsKnown(format))
            return (0, notes);

        foreach (var group in byFolder)
        {
            string destSys = Path.Combine(options.TargetPath, group.Key);
            var games = new List<GamelistGame>();
            foreach (var item in group)
            {
                GameMetadata? meta = null;
                options.Metadata?.TryGetValue(item.GameId, out meta);
                foreach (var entry in item.Plan.GamelistEntries)
                {
                    // Taille réelle du fichier désigné, telle qu'elle est sur la cible (attribut size du .dat Logiqx)
                    string launched = Path.Combine(destSys,
                        GamelistService.NormalizePath(entry.Path).Replace('/', Path.DirectorySeparatorChar));

                    games.Add(new GamelistGame(
                        Path:        entry.Path,
                        Name:        entry.Name,
                        Image:       entry.Image,
                        Description: meta?.Description,
                        Year:        meta?.Year,
                        Developer:   meta?.DeveloperName,
                        Publisher:   meta?.PublisherName,
                        Genre:       meta is { Genres.Count: > 0 } ? string.Join(", ", meta.Genres) : null,
                        Players:     FormatPlayers(meta?.Players),
                        Size:        GetFileSize(launched)));
                }
            }

            // Nom et emplacement du fichier : décidés par le format de l'architecture (dossier système, ou ES-DE/gamelists)
            string relative     = MetadataFormats.RelativePath(format, group.Key);
            string metadataPath = Path.Combine(options.TargetPath, relative.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                var result = MetadataWriterService.Write(format, metadataPath, group.Key, games, options.BackupGamelist);
                written++;
                if (result.BackupPath is not null)
                    notes.Add(string.Format(Strings.Rebase_Gamelist_Backup, Path.GetFileName(result.BackupPath)));
            }
            catch (Exception ex)
            {
                notes.Add(string.Format(Strings.Rebase_Error_GamelistWrite, relative, ErrorMessageClassifier.Classify(ex)));
            }
        }

        return (written, notes);
    }

    /// <summary>"1" pour un joueur, "1-N" au-delà (convention Batocera, acceptée par EmulationStation-fcamod).</summary>
    internal static string? FormatPlayers(int? players)
        => players is null or <= 0 ? null
         : players == 1            ? "1"
         :                           $"1-{players}";

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>Retourne la taille en octets d'un fichier, ou 0 s'il est inaccessible.</summary>
    private static long GetFileSize(string path)
    {
        try   { return new FileInfo(path).Length; }
        catch { return 0; }
    }
}
