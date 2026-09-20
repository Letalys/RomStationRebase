using System.Globalization;
using System.IO;
using System.Text;

namespace RomStationRebase.Services;

/// <summary>Gravité d'une ligne du journal de rebase.</summary>
public enum RebaseLogLevel { Info, Warning, Error }

/// <summary>Journal détaillé d'un rebase. Écrire dedans ne doit jamais faire échouer le rebase.</summary>
public interface IRebaseLog
{
    void Write(RebaseLogLevel level, string message);
}

/// <summary>Journal muet, pour les appels sans fichier (tests, rebase lancé sans journal).</summary>
public sealed class NullRebaseLog : IRebaseLog
{
    public static readonly NullRebaseLog Instance = new();
    public void Write(RebaseLogLevel level, string message) { }
}

public static class RebaseLogExtensions
{
    public static void Info(this IRebaseLog log, string message)  => log.Write(RebaseLogLevel.Info, message);
    public static void Warn(this IRebaseLog log, string message)  => log.Write(RebaseLogLevel.Warning, message);
    public static void Error(this IRebaseLog log, string message) => log.Write(RebaseLogLevel.Error, message);

    /// <summary>Taille lisible, dans les unités de la fenêtre de rebase.</summary>
    public static string Size(long bytes)
    {
        if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F1} GB";
        if (bytes >= 1_048_576)     return $"{bytes / 1_048_576.0:F1} MB";
        if (bytes >= 1024)          return $"{bytes / 1024.0:F0} KB";
        return $"{bytes} B";
    }

    /// <summary>Durée lisible : « 850 ms », « 12,4 s », « 3 min 05 s », « 1 h 02 min ».</summary>
    public static string Duration(TimeSpan span)
    {
        if (span.TotalSeconds < 1)  return $"{span.TotalMilliseconds:F0} ms";
        if (span.TotalSeconds < 60) return $"{span.TotalSeconds:F1} s";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} min {span.Seconds:D2} s";
        return $"{(int)span.TotalHours} h {span.Minutes:D2} min";
    }

    /// <summary>Ligne « libellé : valeur », dans la ponctuation de la langue de l'interface.</summary>
    public static void Pair(this IRebaseLog log, string label, object? value)
        => log.Write(RebaseLogLevel.Info, string.Format(Resources.Strings.Log_KeyValue, label, value));
}

/// <summary>
/// Écrit le journal d'un rebase dans un fichier texte horodaté, une ligne par évènement :
/// date et heure, niveau, message, séparés par des tabulations. Le fichier reste lisible pendant
/// l'écriture (partage en lecture), ce qui permet de le suivre en direct depuis la fenêtre du journal
/// ou depuis un outil comme CMTrace. Appelé depuis plusieurs threads à la fois.
/// </summary>
public sealed class RebaseLogWriter : IRebaseLog, IDisposable
{
    /// <summary>Nombre de journaux conservés ; les plus anciens sont effacés au démarrage d'un rebase.</summary>
    public const int KeptFiles = 30;

    public const string FilePrefix    = "RSR_";
    public const string FileExtension = ".log";

    // Niveaux écrits tels quels dans le fichier, quelle que soit la langue : la fenêtre du journal s'en sert pour colorer
    internal const string InfoToken = "INFO", WarningToken = "WARN", ErrorToken = "ERROR";

    private readonly object _gate = new();
    private StreamWriter?   _writer;

    /// <summary>Dossier des journaux : %LOCALAPPDATA%\RomStationRebase\logs.</summary>
    public static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RomStationRebase", "logs");

    public string FilePath { get; }

    private RebaseLogWriter(string path, StreamWriter writer)
    {
        FilePath = path;
        _writer  = writer;
    }

    /// <summary>
    /// Crée RSR_aaaammjj_hhmmss.log dans <paramref name="directory"/> (dossier des journaux par défaut).
    /// Rend null si le fichier ne peut pas être créé : le rebase a lieu sans journal.
    /// </summary>
    public static RebaseLogWriter? Create(DateTime now, string? directory = null)
    {
        try
        {
            string dir = directory ?? LogDirectory;
            Directory.CreateDirectory(dir);
            Prune(dir, KeptFiles - 1);

            string stem = FilePrefix + now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string path = Path.Combine(dir, stem + FileExtension);
            for (int n = 2; File.Exists(path); n++)
                path = Path.Combine(dir, $"{stem}_{n}{FileExtension}");

            var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read | FileShare.Delete);
            var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)) { AutoFlush = true };
            return new RebaseLogWriter(path, writer);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    public void Write(RebaseLogLevel level, string message)
    {
        string token = level switch
        {
            RebaseLogLevel.Warning => WarningToken,
            RebaseLogLevel.Error   => ErrorToken,
            _                      => InfoToken,
        };
        string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);

        lock (_gate)
        {
            if (_writer is null) return;
            try
            {
                // Un message sur plusieurs lignes (sortie d'un outil, erreur système) donne autant de lignes datées
                foreach (string line in (message ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
                    _writer.WriteLine($"{stamp}\t{token}\t{line.Replace('\t', ' ')}");
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // Disque plein ou support retiré : le journal s'arrête, le rebase continue
                try { _writer.Dispose(); } catch { }
                _writer = null;
            }
        }
    }

    /// <summary>Journal le plus récent du dossier, ou null s'il n'y en a aucun.</summary>
    public static string? LatestLog(string? directory = null)
    {
        try
        {
            string dir = directory ?? LogDirectory;
            if (!Directory.Exists(dir)) return null;
            return Directory.EnumerateFiles(dir, FilePrefix + "*" + FileExtension)
                .OrderByDescending(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Ne garde que les <paramref name="keep"/> journaux les plus récents. Le nom porte la date : l'ordre des noms suffit.</summary>
    internal static void Prune(string directory, int keep)
    {
        try
        {
            var old = Directory.EnumerateFiles(directory, FilePrefix + "*" + FileExtension)
                .OrderByDescending(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                .Skip(Math.Max(0, keep));
            foreach (string file in old)
            {
                try { File.Delete(file); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* ouvert ailleurs : repris au prochain rebase */ }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* dossier inaccessible : rien à ranger */ }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            try { _writer?.Dispose(); } catch { }
            _writer = null;
        }
    }
}
