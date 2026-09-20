using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using RomStationRebase.Models;

namespace RomStationRebase.Services;

/// <summary>
/// Inspecte les archives sources sans les extraire (lecture du répertoire central du zip)
/// et met les résultats en cache par chemin. Seul le zip est géré : la bibliothèque RomStation
/// n'en contient pas d'autre, et une archive illisible est simplement copiée telle quelle.
/// </summary>
public class ArchiveInspector
{
    private static readonly string[] ArchiveExtensions = [".zip", ".7z", ".rar"];

    private readonly ConcurrentDictionary<string, ArchiveInfo> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True si l'extension correspond à une archive (zip, 7z, rar), qu'elle soit extractible ou non.</summary>
    public static bool IsArchive(string path)
        => ArchiveExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>True si RSRebase sait extraire ce format (zip uniquement).</summary>
    public static bool IsExtractable(string path)
        => string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase);

    /// <summary>Retourne l'inspection en cache, ou null si l'archive n'a pas encore été inspectée.</summary>
    public ArchiveInfo? TryGet(string path)
        => _cache.TryGetValue(path, out var info) ? info : null;

    /// <summary>
    /// Inspecte l'archive (ou la renvoie depuis le cache). Ne lève jamais : une archive illisible
    /// retourne IsReadable = false avec sa taille compressée.
    /// </summary>
    public ArchiveInfo Inspect(string path)
        => _cache.GetOrAdd(path, InspectCore);

    private static ArchiveInfo InspectCore(string path)
    {
        long compressed = 0;
        try { compressed = new FileInfo(path).Length; } catch { /* inaccessible : 0 */ }

        if (!IsExtractable(path))
            return new ArchiveInfo { Path = path, CompressedSize = compressed, IsReadable = false };

        try
        {
            using var zip = ZipFile.OpenRead(path);
            var entries = new List<ArchiveEntryInfo>();
            long total  = 0;
            foreach (var e in zip.Entries)
            {
                // Une entrée dossier se termine par un séparateur et n'a pas de contenu
                if (string.IsNullOrEmpty(e.Name)) continue;
                entries.Add(new ArchiveEntryInfo
                {
                    InternalPath = e.FullName,
                    FileName     = e.Name,
                    Length       = e.Length,
                });
                total += e.Length;
            }
            return new ArchiveInfo
            {
                Path             = path,
                CompressedSize   = compressed,
                UncompressedSize = total,
                Entries          = entries,
                IsReadable       = entries.Count > 0,
            };
        }
        catch
        {
            return new ArchiveInfo { Path = path, CompressedSize = compressed, IsReadable = false };
        }
    }
}
