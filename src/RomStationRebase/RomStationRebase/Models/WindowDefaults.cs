namespace RomStationRebase.Models;

/// <summary>Valeurs par défaut des tailles de fenêtres, chargées depuis config/window-defaults.json.</summary>
public class WindowDefaults
{
    public WindowSize MainWindow        { get; set; } = new();
    public WindowSize RebaseWindow      { get; set; } = new();
    public WindowSize GameDetailWindow  { get; set; } = new();
    public WindowSize SettingsWindow    { get; set; } = new();
    public WindowSize ArchitectureEditorWindow { get; set; } = new();
    /// <summary>Valeur par défaut posée ici : un window-defaults.json d'avant la 1.3.0 n'a pas cette entrée et reste valide.</summary>
    public WindowSize ExternalToolsWindow { get; set; } = new() { Width = 960, Height = 700 };
    public WindowSize RebaseLogWindow     { get; set; } = new() { Width = 980, Height = 620 };
}

/// <summary>Taille par défaut d'une fenêtre (la position n'est pas stockée ici — toujours centrée au 1er lancement).</summary>
public class WindowSize
{
    public double Width  { get; set; }
    public double Height { get; set; }
}
