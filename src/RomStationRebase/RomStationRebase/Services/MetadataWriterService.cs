using RomStationRebase.Models;

namespace RomStationRebase.Services;

/// <summary>
/// Écrit le fichier de métadonnées d'un dossier système dans le format que demande l'architecture.
/// Tous les formats fusionnent avec un fichier existant : un second rebase ajoute ses jeux sans effacer ceux du premier.
/// Le nom et l'emplacement du fichier se décident dans <see cref="MetadataFormats"/>, jamais ici.
/// </summary>
public static class MetadataWriterService
{
    public static GamelistWriteResult Write(string format, string filePath, string systemFolder,
        IReadOnlyList<GamelistGame> games, bool backupExisting)
        => Write(format, filePath, systemFolder, games, DateTime.Now, backupExisting);

    internal static GamelistWriteResult Write(string format, string filePath, string systemFolder,
        IReadOnlyList<GamelistGame> games, DateTime now, bool backupExisting)
    {
        switch (MetadataFormats.Normalize(format))
        {
            case MetadataFormats.EmulationStation:
                return GamelistService.WriteOrMerge(filePath, games, now, backupExisting);

            case MetadataFormats.EsDe:
                // ES-DE retrouve ses médias par le nom du jeu : une balise image serait purgée à sa première réécriture
                return GamelistService.WriteOrMerge(filePath,
                    games.Select(g => g with { Image = null }).ToList(), now, backupExisting, withProvider: false);

            case MetadataFormats.Miyoo:
                // Onion ne lit que path, name et image, et ne sait pas désigner un jeu rangé dans un sous-dossier
                return GamelistService.WriteOrMerge(filePath,
                    games.Where(g => !GamelistService.NormalizePath(g.Path).Contains('/'))
                         .Select(g => new GamelistGame(g.Path, g.Name, g.Image, null, null, null, null, null, null))
                         .ToList(),
                    now, backupExisting, withProvider: false);

            case MetadataFormats.Pegasus:
                return PegasusMetadataWriter.WriteOrMerge(filePath, systemFolder, games, now, backupExisting);

            case MetadataFormats.Logiqx:
                return LogiqxDatWriter.WriteOrMerge(filePath, systemFolder, games, now, backupExisting);

            default:
                throw new NotSupportedException($"Format de métadonnées inconnu : {format}");
        }
    }
}
