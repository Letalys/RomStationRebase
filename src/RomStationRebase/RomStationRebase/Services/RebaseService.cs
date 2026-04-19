using System.IO;
using System.Text;
using RomStationRebase.Helpers;
using RomStationRebase.Models;

namespace RomStationRebase.Services;

/// <summary>Exécute le rebase : copie les fichiers ROM sélectionnés vers la structure de dossiers cible.</summary>
public class RebaseService
{
    private const int ChunkSize = 1_048_576; // 1 Mo par chunk

    /// <summary>
    /// Lance le rebase de tous les jeux définis dans options.
    /// Copie en parallèle (SemaphoreSlim), respecte la pause et l'annulation,
    /// génère les fichiers M3U si demandé, et rapporte la progression via IProgress.
    /// </summary>
    public async Task RunRebaseAsync(
        RebaseOptions options,
        IProgress<RebaseProgress> progress,
        CancellationToken ct)
    {
        var archService = new ArchitectureService();
        var items       = BuildRebaseItems(options);

        long totalBytes  = items.Sum(item => item.SourceFilePaths.Sum(GetFileSize));
        long copiedBytes = 0;
        int  completed   = 0;
        int  failed      = 0;
        int  skipped     = 0;

        var semaphore             = new SemaphoreSlim(options.MaxParallelCopies, options.MaxParallelCopies);
        var startTime             = DateTime.UtcNow;
        double lastReportedProgress = 0.0; // throttle basé sur le delta de pourcentage

        // Rapport de progression — appelé depuis n'importe quel thread (Progress<T> marshale sur UI)
        void Report(RebaseGameItem? current = null)
        {
            var elapsed    = (DateTime.UtcNow - startTime).TotalSeconds;
            long copied    = Interlocked.Read(ref copiedBytes);
            double speed   = elapsed > 0 ? copied / elapsed : 0;
            long remaining = totalBytes - copied;
            var eta        = speed > 0 ? TimeSpan.FromSeconds(remaining / speed) : TimeSpan.Zero;

            progress.Report(new RebaseProgress
            {
                TotalFiles             = items.Count,
                CompletedFiles         = completed,
                FailedFiles            = failed,
                SkippedFiles           = skipped,
                TotalBytes             = totalBytes,
                CopiedBytes            = copied,
                SpeedBytesPerSecond    = speed,
                EstimatedTimeRemaining = eta,
                CurrentItem            = current,
            });
        }

        // Task.Run obligatoire : les lambdas async démarrées depuis le thread UI y restent si WaitAsync
        // complète synchroniquement. PauseEvent.Wait bloquerait alors le thread UI.
        var tasks = items.Select(item => Task.Run(async () =>
        {
            await semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                options.PauseEvent.Wait(ct); // sûr : exécuté sur un thread pool
                ct.ThrowIfCancellationRequested();

                item.Status = RebaseItemStatus.Copying;
                Report(item);

                string? targetFolder = archService.GetTargetFolder(options.Mapping, item.SystemName);
                if (targetFolder is null)
                {
                    // Système sans mapping → on saute le jeu
                    item.Status    = RebaseItemStatus.Skipped;
                    item.IsSkipped = true;
                    Interlocked.Increment(ref skipped);
                    Report(item);
                    return;
                }

                string destDir = Path.Combine(options.TargetPath, targetFolder);
                Directory.CreateDirectory(destDir);

                var copiedNames  = new List<string>();
                string cleanTitle = SanitizeTitle(item.Title);
                int totalSrc      = item.SourceFilePaths.Count;
                int ignoredCount  = 0;
                try
                {
                    for (int fileIdx = 0; fileIdx < totalSrc; fileIdx++)
                    {
                        string src = item.SourceFilePaths[fileIdx];
                        ct.ThrowIfCancellationRequested();
                        options.PauseEvent.Wait(ct);

                        if (!System.IO.File.Exists(src))
                        {
                            item.Status      = RebaseItemStatus.Failed;
                            item.ErrorDetail = $"Source introuvable : {Path.GetFileName(src)}";
                            Interlocked.Increment(ref failed);
                            Report(item);
                            return;
                        }

                        // Nom du fichier destination basé sur le titre du jeu
                        string ext      = Path.GetExtension(src);
                        string newName  = totalSrc == 1
                            ? cleanTitle + ext
                            : $"{cleanTitle} (Disc {fileIdx + 1}){ext}";
                        string dest     = Path.Combine(destDir, newName);

                        if (System.IO.File.Exists(dest) && options.DuplicatePolicy == DuplicatePolicy.Ignore)
                        {
                            ignoredCount++;
                            copiedNames.Add(newName);
                            continue;
                        }

                        // Throttle par delta de 0.5% — évite que les threads parallèles
                        // se bloquent mutuellement avec le throttle temporel (race sur lastReport)
                        var bytesProgress = new Progress<long>(bytes =>
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

                        await CopyFileWithProgressAsync(src, dest, bytesProgress,
                            options.RetryCount, options.RetryDelaySeconds, ct).ConfigureAwait(false);
                        copiedNames.Add(newName);
                    }

                    // Génération du fichier M3U pour les jeux multi-disques
                    if (options.GenerateM3U && copiedNames.Count > 1)
                    {
                        string m3uPath = Path.Combine(destDir, cleanTitle + ".m3u");
                        await System.IO.File.WriteAllTextAsync(m3uPath,
                            GenerateM3UContent(item.Title, copiedNames), ct).ConfigureAwait(false);
                    }

                    // Tous les fichiers ignorés par la politique Ignore → statut Skipped
                    if (ignoredCount == totalSrc && totalSrc > 0)
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
        Report(); // rapport final agrégé
    }

    /// <summary>
    /// Copie un fichier par chunks de 1 Mo avec retry en cas d'échec.
    /// Chaque chunk est rapporté via bytesProgress.
    /// </summary>
    internal async Task CopyFileWithProgressAsync(
        string source, string dest,
        IProgress<long> bytesProgress,
        int retryCount, int retryDelaySeconds,
        CancellationToken ct)
    {
        for (int attempt = 0; attempt <= retryCount; attempt++)
        {
            try
            {
                await CopyFileInternalAsync(source, dest, bytesProgress, ct).ConfigureAwait(false);
                return; // succès
            }
            catch (Exception) when (attempt < retryCount && !ct.IsCancellationRequested)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Rebase] Retry {attempt + 1}/{retryCount} for {System.IO.Path.GetFileName(source)}");
                await Task.Delay(retryDelaySeconds * 1000, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Copie effective du fichier par chunks de 1 Mo — appelée par CopyFileWithProgressAsync.</summary>
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
    /// Le fichier liste les noms de fichiers ZIP, un par ligne, précédés d'un commentaire titre.
    /// </summary>
    public string GenerateM3UContent(string gameTitle, List<string> zipFileNames)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {gameTitle}");
        foreach (var file in zipFileNames)
            sb.AppendLine(file);
        return sb.ToString();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>Construit la liste des RebaseGameItem depuis les jeux sélectionnés dans les options.</summary>
    private static List<RebaseGameItem> BuildRebaseItems(RebaseOptions options)
    {
        var items = new List<RebaseGameItem>();
        foreach (var game in options.SelectedGames)
        {
            if (string.IsNullOrEmpty(game.GameDirectory)) continue;

            string gameRoot = ArchitectureService.ResolveGameRoot(game.GameDirectory, options.RomStationPath);
            if (!Directory.Exists(gameRoot)) continue;

            // Tous les fichiers du jeu sauf le dossier images/ (jaquettes)
            var files = Directory
                .GetFiles(gameRoot, "*", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "images" + Path.DirectorySeparatorChar))
                .OrderBy(f => f)
                .ToList();

            if (files.Count == 0) continue;

            items.Add(new RebaseGameItem
            {
                GameId          = game.Id,
                Title           = game.Title,
                SystemName      = game.SystemName,
                SystemImagePath = game.SystemImagePath,
                FileCount       = files.Count,
                SourceFilePaths = files,
            });
        }
        return items;
    }

    /// <summary>Retourne la taille en octets d'un fichier, ou 0 s'il est inaccessible.</summary>
    private static long GetFileSize(string path)
    {
        try   { return new FileInfo(path).Length; }
        catch { return 0; }
    }

    /// <summary>
    /// Remplace les caractères interdits Windows dans un titre de jeu par "-" pour former
    /// un nom de fichier valide. Élimine les tirets multiples et trim les tirets en bordure.
    /// </summary>
    private static string SanitizeTitle(string title)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            title = title.Replace(c, '-');
        while (title.Contains("--"))
            title = title.Replace("--", "-");
        return title.Trim('-');
    }
}
