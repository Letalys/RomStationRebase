namespace RomStationRebase.Helpers;

/// <summary>
/// Levée quand un fichier de configuration JSON est malformé ou contient une valeur invalide.
/// Contient le nom du fichier et le détail de l'erreur pour affichage dans le dialog de confirmation.
/// </summary>
public class ConfigCorruptedException : Exception
{
    /// <summary>Nom du fichier de configuration concerné (ex : "app-state.json").</summary>
    public string FileName { get; }

    /// <summary>Description précise de l'erreur (champ invalide, JSON malformé, etc.).</summary>
    public string Detail { get; }

    public ConfigCorruptedException(string fileName, string detail)
        : base($"Configuration file '{fileName}' is corrupted: {detail}")
    {
        FileName = fileName;
        Detail   = detail;
    }
}
