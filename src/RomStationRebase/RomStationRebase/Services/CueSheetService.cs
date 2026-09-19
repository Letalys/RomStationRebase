using System.IO;
using System.Text;

namespace RomStationRebase.Services;

/// <summary>
/// Écrit le fichier .cue minimal d'une image disque brute livrée seule.
/// Un .bin sans .cue n'est listé par aucun émulateur CD (constaté sous dArkOS) : la piste unique
/// est décrite d'après l'en-tête du premier secteur, sans jamais toucher au .bin.
/// </summary>
public static class CueSheetService
{
    /// <summary>Motif de synchronisation qui ouvre tout secteur brut de 2352 octets.</summary>
    private static readonly byte[] SectorSync =
        [0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00];

    /// <summary>
    /// Mode de la piste de données : "MODE2/2352" (Playstation), "MODE1/2352" (Saturn, Mega-CD, PC Engine CD),
    /// ou "MODE1/2048" quand l'image n'a pas d'en-tête de secteur (image ISO renommée).
    /// </summary>
    public static string DetectTrackMode(string binPath)
    {
        var header = new byte[16];
        using (var stream = new FileStream(binPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length)
                return "MODE1/2048";
        }

        if (!header.AsSpan(0, SectorSync.Length).SequenceEqual(SectorSync))
            return "MODE1/2048";

        // Octet 15 : mode du secteur (1 ou 2)
        return header[15] == 2 ? "MODE2/2352" : "MODE1/2352";
    }

    /// <summary>Contenu d'un .cue à piste unique désignant le .bin par son seul nom de fichier.</summary>
    public static string BuildContent(string binFileName, string trackMode)
    {
        var sb = new StringBuilder();
        sb.Append("FILE \"").Append(binFileName).Append("\" BINARY\r\n");
        sb.Append("  TRACK 01 ").Append(trackMode).Append("\r\n");
        sb.Append("    INDEX 01 00:00:00\r\n");
        return sb.ToString();
    }

    /// <summary>Écrit le .cue à côté du .bin déjà présent sur la cible. Le .cue et le .bin doivent partager leur dossier.</summary>
    public static void WriteFor(string binPath, string cuePath)
    {
        string content = BuildContent(Path.GetFileName(binPath), DetectTrackMode(binPath));
        File.WriteAllText(cuePath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
