namespace RomStationRebase.Models;

/// <summary>État persistant d'une fenêtre : position, taille et statut maximisé.</summary>
public class WindowBounds
{
    /// <summary>Position horizontale du coin haut-gauche.</summary>
    public double Left { get; set; }

    /// <summary>Position verticale du coin haut-gauche.</summary>
    public double Top { get; set; }

    /// <summary>Largeur de la fenêtre en mode normal (non maximisé).</summary>
    public double Width { get; set; }

    /// <summary>Hauteur de la fenêtre en mode normal (non maximisé).</summary>
    public double Height { get; set; }

    /// <summary>True si la fenêtre était maximisée lors de la dernière fermeture.</summary>
    public bool IsMaximized { get; set; }
}
