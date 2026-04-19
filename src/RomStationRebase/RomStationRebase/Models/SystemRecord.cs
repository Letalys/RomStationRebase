namespace RomStationRebase.Models;

/// <summary>Représente un système (console) tel que lu depuis la table APP.SYSTEM de la base Derby.</summary>
public class SystemRecord
{
    /// <summary>Identifiant Derby du système.</summary>
    public int Id { get; init; }

    /// <summary>Nom du système.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Chemin de l'image associée au système dans l'installation RomStation. Null si absent.</summary>
    public string? ImagePath { get; init; }
}
