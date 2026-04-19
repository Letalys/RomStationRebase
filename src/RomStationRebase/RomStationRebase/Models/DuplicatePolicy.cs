namespace RomStationRebase.Models;

/// <summary>Comportement en cas de fichier déjà présent dans le dossier cible lors du rebase.</summary>
public enum DuplicatePolicy
{
    /// <summary>Ignore le fichier existant et passe au suivant.</summary>
    Ignore,
    /// <summary>Écrase le fichier existant.</summary>
    Overwrite
}
