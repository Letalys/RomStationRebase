namespace RomStationRebase.Models;

/// <summary>Table de correspondance système RomStation → dossier cible, désérialisée depuis un fichier de mapping JSON.</summary>
public class FolderTreeMapping
{
    public List<SystemMapping> FolderTreeMappings { get; set; } = [];
}

/// <summary>Association entre un nom de système RomStation et le nom du dossier cible (ex : "Playstation" → "psx").</summary>
public class SystemMapping
{
    public string RomStationSystem { get; set; } = string.Empty;
    public string TargetFolder     { get; set; } = string.Empty;
}
