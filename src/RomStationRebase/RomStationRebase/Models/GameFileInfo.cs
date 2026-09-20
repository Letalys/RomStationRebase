namespace RomStationRebase.Models;

/// <summary>
/// Un fichier de jeu tel que décrit par la base Derby : libellé de APP.GAME_FILE et chemin réel de APP.GAME_PROFILE.
/// C'est la source de vérité du nommage 1.3.0 — le nom d'origine de l'archive et le libellé (disque, version) y sont lus,
/// plus jamais devinés depuis le disque.
/// </summary>
public sealed class GameFileInfo
{
    /// <summary>Identifiant Derby du jeu propriétaire (APP.GAME.ID).</summary>
    public int GameId { get; init; }

    /// <summary>Identifiant Derby du fichier (APP.GAME_FILE.ID).</summary>
    public int FileId { get; init; }

    /// <summary>Libellé du fichier, ex : "Chrono Cross (Disc 1)" ou "Breath of Fire IV (v1.0a PAL)".</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Chemin du fichier relatif au dossier app de RomStation (APP.GAME_PROFILE.PATH), séparateurs Windows.</summary>
    public string RelativePath { get; init; } = string.Empty;

    /// <summary>Dossier du fichier relatif au dossier app (APP.GAME_FILE.DIRECTORY).</summary>
    public string RelativeDirectory { get; init; } = string.Empty;
}
