using System.IO;
using System.IO.Compression;

namespace RomStationRebase.Services;

/// <summary>
/// Levée quand une entrée d'archive tente de s'écrire hors du dossier de destination
/// (chemin absolu, lettre de lecteur ou remontée par "..") — attaque dite « zip slip ».
/// </summary>
public class UnsafeArchiveException : Exception
{
    /// <summary>Chemin interne de l'entrée refusée, tel que stocké dans l'archive.</summary>
    public string Entry { get; }

    /// <summary>Construit l'exception pour l'entrée refusée.</summary>
    public UnsafeArchiveException(string entry)
        : base($"L'entrée d'archive « {entry} » pointe hors du dossier de destination.")
    {
        Entry = entry;
    }
}

/// <summary>
/// Extrait une archive zip vers un dossier de destination, avec progression par octets décompressés,
/// annulation coopérative et nettoyage de tout ce qui a été écrit en cas d'échec.
/// Seul le zip est géré (cf. <see cref="ArchiveInspector.IsExtractable"/>).
/// </summary>
public static class ArchiveExtractor
{
    /// <summary>Taille des blocs de lecture/écriture (1 Mo) — un rapport de progression par bloc.</summary>
    private const int BlockSize = 1024 * 1024;

    /// <summary>
    /// Extrait une archive zip.
    /// <list type="bullet">
    /// <item><paramref name="destDir"/> : dossier absolu de dépôt, créé si absent.</item>
    /// <item><paramref name="singleEntryTargetName"/> : si non null, l'archive est réputée n'avoir qu'une entrée fichier,
    /// écrite sous ce nom dans <paramref name="destDir"/> (renommage). Si elle en a en fait un autre nombre,
    /// <see cref="InvalidDataException"/> est levée.</item>
    /// <item>Sinon chaque entrée fichier est écrite sous son chemin interne relatif à <paramref name="destDir"/>
    /// (sous-dossiers créés), noms internes conservés.</item>
    /// <item><paramref name="bytesProgress"/> reçoit le nombre d'octets décompressés après chaque bloc écrit.</item>
    /// <item>Sécurité : toutes les entrées sont vérifiées avant la moindre écriture ; une entrée qui sortirait
    /// du dossier de destination lève <see cref="UnsafeArchiveException"/>.</item>
    /// <item>Un fichier cible existant est remplacé.</item>
    /// <item>En cas d'annulation ou d'échec, les fichiers écrits par cet appel sont supprimés, puis les dossiers
    /// qu'il a créés et qui sont restés vides (y compris <paramref name="destDir"/>), et l'exception est relancée.</item>
    /// <item>Les entrées dossier (nom vide) ne sont jamais extraites.</item>
    /// </list>
    /// </summary>
    public static async Task ExtractAsync(
        string archivePath,
        string destDir,
        string? singleEntryTargetName,
        IProgress<long> bytesProgress,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Racine normalisée sans séparateur final, et préfixe avec séparateur pour le test de confinement :
        // "D:\out" ne doit pas accepter "D:\outside\x".
        string root   = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destDir));
        string prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;

        using var zip = ZipFile.OpenRead(archivePath);

        // Seules les entrées fichier comptent : une entrée dossier a un Name vide.
        var fileEntries = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();

        // Phase 1 : résolution et vérification de toutes les cibles avant d'écrire quoi que ce soit.
        var targets = new List<(ZipArchiveEntry Entry, string FullPath)>(fileEntries.Count);
        foreach (var entry in fileEntries)
        {
            string relative = singleEntryTargetName ?? entry.FullName;
            targets.Add((entry, ResolveSafeTarget(prefix, relative)));
        }

        if (singleEntryTargetName is not null && fileEntries.Count != 1)
            throw new InvalidDataException(
                $"L'archive « {archivePath} » contient {fileEntries.Count} entrée(s) fichier alors qu'une seule était attendue.");

        // Phase 2 : écriture, avec traçabilité de tout ce qui est créé pour pouvoir le défaire.
        var writtenFiles = new List<string>();
        var createdDirs  = new List<string>();
        var buffer       = new byte[BlockSize];

        try
        {
            foreach (var (entry, fullPath) in targets)
            {
                ct.ThrowIfCancellationRequested();

                EnsureDirectory(Path.GetDirectoryName(fullPath)!, root, createdDirs);

                // Le fichier est tracé avant son ouverture : un fichier tronqué par FileMode.Create
                // puis abandonné doit être nettoyé comme un fichier partiel.
                writtenFiles.Add(fullPath);

                await using var source = entry.Open();
                await using var target = new FileStream(
                    fullPath, FileMode.Create, FileAccess.Write, FileShare.None, BlockSize, useAsync: true);

                int read;
                while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    bytesProgress.Report(read);
                    ct.ThrowIfCancellationRequested();
                }
            }
        }
        catch
        {
            Cleanup(writtenFiles, createdDirs);
            throw;
        }
    }

    /// <summary>
    /// Résout le chemin cible d'une entrée sous <paramref name="prefix"/> et refuse tout ce qui en sortirait :
    /// chemin absolu, lettre de lecteur, ou remontée par ".." (vérifiée après normalisation par GetFullPath).
    /// </summary>
    private static string ResolveSafeTarget(string prefix, string relativePath)
    {
        string normalized = relativePath.Replace('/', Path.DirectorySeparatorChar)
                                        .Replace('\\', Path.DirectorySeparatorChar);

        // Path.Combine renvoie le second argument tel quel s'il est rooté : à refuser explicitement.
        if (string.IsNullOrWhiteSpace(normalized) || Path.IsPathRooted(normalized) || normalized.Contains(':'))
            throw new UnsafeArchiveException(relativePath);

        string full = Path.GetFullPath(Path.Combine(prefix, normalized));
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new UnsafeArchiveException(relativePath);

        return full;
    }

    /// <summary>
    /// Crée <paramref name="dir"/> et ses ancêtres manquants jusqu'à <paramref name="root"/> inclus,
    /// en enregistrant chaque dossier créé (du plus haut au plus profond) pour le nettoyage.
    /// </summary>
    private static void EnsureDirectory(string dir, string root, List<string> createdDirs)
    {
        if (Directory.Exists(dir)) return;

        var missing = new Stack<string>();
        string? current = dir;
        while (current is not null && !Directory.Exists(current))
        {
            missing.Push(current);
            if (string.Equals(current, root, StringComparison.OrdinalIgnoreCase)) break;
            current = Path.GetDirectoryName(current);
        }

        while (missing.Count > 0)
        {
            string toCreate = missing.Pop();
            Directory.CreateDirectory(toCreate);
            createdDirs.Add(toCreate);
        }
    }

    /// <summary>
    /// Défait ce que l'appel a écrit : fichiers d'abord, puis dossiers créés en ordre inverse
    /// et seulement s'ils sont vides. Les erreurs de nettoyage sont ignorées pour ne pas masquer l'exception d'origine.
    /// </summary>
    private static void Cleanup(List<string> writtenFiles, List<string> createdDirs)
    {
        for (int i = writtenFiles.Count - 1; i >= 0; i--)
        {
            try { File.Delete(writtenFiles[i]); }
            catch { /* meilleur effort */ }
        }

        for (int i = createdDirs.Count - 1; i >= 0; i--)
        {
            try
            {
                string dir = createdDirs[i];
                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir);
            }
            catch { /* meilleur effort */ }
        }
    }
}
