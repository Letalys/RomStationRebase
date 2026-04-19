namespace RomStationRebase.Models;

/// <summary>Index des architectures cibles disponibles, désérialisé depuis config/architectures/index.json.</summary>
public class ArchitectureIndex
{
    public List<ArchitectureEntry> Architectures { get; set; } = [];
}

/// <summary>Entrée d'une architecture cible : identifiant, libellé, fichier de mapping et options par défaut.</summary>
public class ArchitectureEntry
{
    public string Id                  { get; set; } = string.Empty;
    public string Label               { get; set; } = string.Empty;
    public string Description         { get; set; } = string.Empty;
    public string FolderTreeMapping   { get; set; } = string.Empty;
    public bool   GenerateM3UByDefault { get; set; }
    public bool   IsDefault           { get; set; }
}
