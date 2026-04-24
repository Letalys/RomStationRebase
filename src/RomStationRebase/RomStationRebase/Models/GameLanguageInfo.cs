namespace RomStationRebase.Models;

/// <summary>Langue d'un jeu avec son drapeau, telle que lue depuis GAME_LANGUAGE + LANGUAGE.</summary>
public sealed class GameLanguageInfo
{
    /// <summary>Identifiant Derby de la langue.</summary>
    public int Id { get; init; }

    /// <summary>Nom traduit de la langue dans la locale demandée.</summary>
    public required string Name { get; init; }

    /// <summary>Chemin absolu vers l'image du drapeau. Null si absent.</summary>
    public string? FlagImagePath { get; init; }

    /// <summary>True si l'image du drapeau existe physiquement sur le disque.</summary>
    public bool FlagExists { get; init; }
}
