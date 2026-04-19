namespace RomStationRebase.Models;

/// <summary>
/// Métadonnées distribuées avec l'application (fichier config/app-metadata.json).
/// Contient les informations fixes livrées avec le programme :
/// URL du repo, auteur, licence. La version est lue directement depuis
/// l'assembly, pas depuis ce fichier.
/// </summary>
public class AppMetadata
{
    /// <summary>URL du dépôt GitHub du projet.</summary>
    public string RepositoryUrl { get; set; } = string.Empty;

    /// <summary>Nom de l'auteur du projet.</summary>
    public string Author { get; set; } = string.Empty;

    /// <summary>Nom de la licence sous laquelle le projet est distribué.</summary>
    public string License { get; set; } = string.Empty;
}
