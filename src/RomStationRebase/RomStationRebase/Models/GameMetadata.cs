namespace RomStationRebase.Models;

/// <summary>
/// Métadonnées d'un jeu destinées aux fichiers de métadonnées de la cible (gamelist.xml).
/// Chargées en lot par DerbyService.GetGamelistMetadata — jamais fiche par fiche.
/// </summary>
public sealed class GameMetadata
{
    public int     GameId        { get; init; }
    public string  Title         { get; init; } = string.Empty;
    public string? Description   { get; init; }
    public int?    Year          { get; init; }
    public int?    Players       { get; init; }
    public string? DeveloperName { get; init; }
    public string? PublisherName { get; init; }
    /// <summary>Genres dans la locale demandée, repli anglais par genre, triés alphabétiquement.</summary>
    public IReadOnlyList<string> Genres { get; init; } = [];
}
