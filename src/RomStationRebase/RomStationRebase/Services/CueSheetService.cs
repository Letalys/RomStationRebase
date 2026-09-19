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

    /// <summary>
    /// Répare un .cue dont la ligne FILE cite un fichier absent. Beaucoup d'archives de RomStation contiennent un .bin
    /// renommé et un .cue resté sur l'ancien nom (« ALUNDRA_PAL.BIN » pour « Alundra.bin ») : certains émulateurs s'en
    /// accommodent, chdman non. La correction n'est faite que si elle est sans ambiguïté : une seule ligne FILE, et une
    /// seule image (.bin ou .img) dans le dossier. Le fichier est traité en octets, son encodage n'est pas touché.
    /// Renvoie true si le .cue a été réécrit.
    /// </summary>
    public static bool RepairFileReference(string cuePath)
    {
        try
        {
            string? folder = Path.GetDirectoryName(cuePath);
            if (folder is null || !File.Exists(cuePath)) return false;

            // Latin-1 : un octet par caractère dans les deux sens, quel que soit l'encodage réel du fichier
            string text = Encoding.Latin1.GetString(File.ReadAllBytes(cuePath));
            var refs = System.Text.RegularExpressions.Regex.Matches(text, "FILE\\s+\"([^\"]+)\"",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (refs.Count != 1) return false;

            string citedRaw = refs[0].Groups[1].Value;
            string cited    = Path.GetFileName(Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(citedRaw)).Replace('\\', '/'));
            if (File.Exists(Path.Combine(folder, cited))
                || File.Exists(Path.Combine(folder, Path.GetFileName(citedRaw.Replace('\\', '/'))))) return false;

            var images = Directory.EnumerateFiles(folder)
                .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".bin" or ".img")
                .ToList();
            if (images.Count != 1) return false;

            string name = Encoding.Latin1.GetString(Encoding.UTF8.GetBytes(Path.GetFileName(images[0])));
            var group = refs[0].Groups[1];
            string repaired = text[..group.Index] + name + text[(group.Index + group.Length)..];
            File.WriteAllBytes(cuePath, Encoding.Latin1.GetBytes(repaired));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false; // réparation de confort : le jeu est copié tel que RomStation le livre
        }
    }

    /// <summary>Écrit le .cue à côté du .bin déjà présent sur la cible. Le .cue et le .bin doivent partager leur dossier.</summary>
    public static void WriteFor(string binPath, string cuePath)
    {
        string content = BuildContent(Path.GetFileName(binPath), DetectTrackMode(binPath));
        File.WriteAllText(cuePath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
