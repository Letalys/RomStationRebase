namespace RomStationRebase.Models;

/// <summary>Représente un jeu tel que lu depuis la table APP.GAME de la base Derby.</summary>
public class GameRecord
{
    /// <summary>Identifiant Derby du jeu.</summary>
    public int Id { get; init; }

    /// <summary>Titre du jeu (colonne TITLE de APP.GAME).</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Nom du système auquel appartient ce jeu.</summary>
    public string SystemName { get; init; } = string.Empty;

    /// <summary>Chemin de l'image du système dans l'installation RomStation. Null si absent.</summary>
    public string? SystemImagePath { get; init; }

    /// <summary>Répertoire racine du jeu dans l'installation RomStation (dérivé de GAME_FILE.DIRECTORY).</summary>
    public string GameDirectory { get; init; } = string.Empty;

    /// <summary>Nombre de fichiers ROM associés à ce jeu (multi-disques, hack, etc.).</summary>
    public int FileCount { get; init; }

    /// <summary>Chemin calculé vers la jaquette (cover.png) dans l'installation RomStation. Null si non calculable.</summary>
    public string? CoverPath { get; init; }

    /// <summary>True si le fichier de jaquette existe physiquement sur le disque.</summary>
    public bool CoverExists { get; init; }

    /// <summary>True si au moins un fichier ROM existe physiquement sur le disque.</summary>
    public bool FileExists { get; init; }
}
