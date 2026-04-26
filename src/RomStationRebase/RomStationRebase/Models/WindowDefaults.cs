namespace RomStationRebase.Models;

/// <summary>Valeurs par défaut des tailles de fenêtres, chargées depuis config/window-defaults.json.</summary>
public class WindowDefaults
{
    public WindowSize MainWindow        { get; set; } = new();
    public WindowSize RebaseWindow      { get; set; } = new();
    public WindowSize GameDetailWindow  { get; set; } = new();
    public WindowSize SettingsWindow    { get; set; } = new();
}

/// <summary>Taille par défaut d'une fenêtre (la position n'est pas stockée ici — toujours centrée au 1er lancement).</summary>
public class WindowSize
{
    public double Width  { get; set; }
    public double Height { get; set; }
}
