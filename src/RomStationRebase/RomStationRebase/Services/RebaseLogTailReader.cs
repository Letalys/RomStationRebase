using System.Globalization;
using System.IO;
using System.Text;

namespace RomStationRebase.Services;

/// <summary>Ligne du journal telle que la fenêtre du journal l'affiche.</summary>
/// <param name="Time">Heure de la ligne, vide pour une ligne sans horodatage (fichier étranger, suite de message).</param>
public sealed record RebaseLogLine(string Time, RebaseLogLevel Level, string Message)
{
    public bool IsProblem => Level != RebaseLogLevel.Info;

    /// <summary>Découpe « date heure, niveau, message » séparés par des tabulations. Toute autre ligne est rendue telle quelle, en information.</summary>
    public static RebaseLogLine Parse(string raw)
    {
        string[] parts = raw.Split('\t', 3);
        if (parts.Length == 3
            && DateTime.TryParseExact(parts[0], "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out var stamp))
        {
            var level = parts[1] switch
            {
                RebaseLogWriter.WarningToken => RebaseLogLevel.Warning,
                RebaseLogWriter.ErrorToken   => RebaseLogLevel.Error,
                _                            => RebaseLogLevel.Info,
            };
            return new RebaseLogLine(stamp.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture), level, parts[2]);
        }
        return new RebaseLogLine(string.Empty, RebaseLogLevel.Info, raw);
    }
}

/// <summary>
/// Lit un journal au fur et à mesure qu'il s'écrit : chaque appel à <see cref="ReadNew"/> rend les lignes complètes
/// ajoutées depuis l'appel précédent. Le fichier est ouvert en partage total, il peut donc être en cours d'écriture.
/// Une ligne à moitié écrite attend l'appel suivant ; un fichier raccourci ou remplacé est relu depuis le début.
/// </summary>
public sealed class RebaseLogTailReader(string path)
{
    private readonly Decoder       _decoder = new UTF8Encoding(false).GetDecoder();
    private readonly StringBuilder _pending = new();
    private long _position;

    public string FilePath { get; } = path;

    /// <summary>True quand le dernier appel a constaté que le fichier repartait de zéro : l'affichage doit être vidé.</summary>
    public bool WasReset { get; private set; }

    public IReadOnlyList<RebaseLogLine> ReadNew()
    {
        WasReset = false;
        var lines = new List<RebaseLogLine>();
        try
        {
            using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read,
                                              FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length < _position)
            {
                _position = 0;
                _pending.Clear();
                _decoder.Reset();
                WasReset = true;
            }
            if (stream.Length == _position) return lines;

            stream.Seek(_position, SeekOrigin.Begin);
            var bytes = new byte[81920];
            var chars = new char[bytes.Length + 8]; // marge pour les octets que le décodeur gardait de la lecture précédente
            int read;
            while ((read = stream.Read(bytes, 0, bytes.Length)) > 0)
            {
                // Le décodeur garde pour lui un caractère accentué coupé entre deux lectures
                int count = _decoder.GetChars(bytes, 0, read, chars, 0, flush: false);
                _pending.Append(chars, 0, count);
                _position += read;
            }

            string text = _pending.ToString();
            int lastBreak = text.LastIndexOf('\n');
            if (lastBreak < 0) return lines;

            _pending.Clear();
            _pending.Append(text, lastBreak + 1, text.Length - lastBreak - 1);
            foreach (string raw in text[..lastBreak].Split('\n'))
            {
                string line = raw.TrimEnd('\r').TrimStart('﻿');
                if (line.Length > 0) lines.Add(RebaseLogLine.Parse(line));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Fichier momentanément inaccessible ou supprimé : on réessaiera au prochain passage
        }
        return lines;
    }
}
