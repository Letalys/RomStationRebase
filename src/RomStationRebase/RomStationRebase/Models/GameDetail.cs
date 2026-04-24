namespace RomStationRebase.Models;

/// <summary>Fiche détaillée d'un jeu, chargée à la demande depuis la base Derby.</summary>
public sealed class GameDetail
{
    /// <summary>Identifiant Derby du jeu.</summary>
    public int Id { get; init; }

    /// <summary>Titre du jeu.</summary>
    public required string Title { get; init; }

    /// <summary>Année de sortie. Null si non renseignée.</summary>
    public int? Year { get; init; }

    /// <summary>Nombre de joueurs. Null si non renseigné.</summary>
    public int? Players { get; init; }

    /// <summary>Nom du développeur. Null si non renseigné.</summary>
    public string? DeveloperName { get; init; }

    /// <summary>Nom de l'éditeur. Null si non renseigné.</summary>
    public string? PublisherName { get; init; }

    /// <summary>Nom du système auquel appartient ce jeu.</summary>
    public required string SystemName { get; init; }

    /// <summary>Chemin absolu vers l'image du système. Null si absent.</summary>
    public string? SystemImagePath { get; init; }

    /// <summary>Chemin absolu vers la jaquette (cover.png). Null si non calculable.</summary>
    public string? CoverPath { get; init; }

    /// <summary>True si la jaquette existe physiquement sur le disque.</summary>
    public bool CoverExists { get; init; }

    /// <summary>Répertoire racine du jeu tel que stocké dans GAME.DIRECTORY (chemin relatif).</summary>
    public required string Directory { get; init; }

    /// <summary>Description du jeu dans la locale demandée, avec fallback vers "en". Null si absente.</summary>
    public string? Description { get; init; }

    /// <summary>Liste des genres dans la locale demandée, triés alphabétiquement.</summary>
    public required IReadOnlyList<string> Genres { get; init; }

    /// <summary>Liste des langues du jeu avec drapeau.</summary>
    public required IReadOnlyList<GameLanguageInfo> Languages { get; init; }

    /// <summary>URL RomStation du jeu (lien EXTERNAL=false). Null si absent.</summary>
    public string? RomStationUrl { get; init; }

    /// <summary>Locale réellement demandée lors du chargement ("en" ou "fr").</summary>
    public required string RequestedLocale { get; init; }

    /// <summary>True si la description retournée est dans la locale de fallback ("en") plutôt que la locale demandée.</summary>
    public bool DescriptionIsFallback { get; init; }

    /// <summary>True si au moins un genre a été retourné dans la locale de fallback ("en").</summary>
    public bool GenresHasFallback { get; init; }
}
