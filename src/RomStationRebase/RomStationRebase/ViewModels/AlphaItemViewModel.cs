namespace RomStationRebase.ViewModels;

/// <summary>Représente une lettre (ou #) dans l'abécédaire de navigation rapide.</summary>
public class AlphaItemViewModel : ViewModelBase
{
    private bool _isEnabled;

    /// <summary>Lettre ("A" à "Z") ou "#" (pour les titres ne commençant pas par A-Z).</summary>
    public string Letter { get; }

    /// <summary>
    /// True si au moins un jeu de FilteredGames commence par cette lettre
    /// et que le tri alphabétique par titre est actif.
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public AlphaItemViewModel(string letter, bool isEnabled = false)
    {
        Letter     = letter;
        _isEnabled = isEnabled;
    }
}
