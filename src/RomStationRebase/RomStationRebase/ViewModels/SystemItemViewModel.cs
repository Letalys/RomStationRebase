namespace RomStationRebase.ViewModels;

/// <summary>Représente un système (console) dans la sidebar, avec sa case à cocher.</summary>
public class SystemItemViewModel : ViewModelBase
{
    private readonly Action _onFilterChanged;
    private bool _isChecked;

    /// <summary>Nom du système tel qu'il apparaît dans la base RomStation.</summary>
    public string Name { get; }

    /// <summary>Chemin vers l'image du système dans l'installation RomStation. Null si absent.</summary>
    public string? ImagePath { get; }

    /// <summary>Nombre total de jeux pour ce système dans la bibliothèque.</summary>
    public int GameCount { get; }

    /// <summary>
    /// Indique si ce système est inclus dans le filtrage.
    /// Tout changement déclenche le recalcul de FilteredGames dans MainViewModel.
    /// </summary>
    public bool IsChecked
    {
        get => _isChecked;
        set { if (SetProperty(ref _isChecked, value)) _onFilterChanged(); }
    }

    /// <param name="name">Nom du système.</param>
    /// <param name="imagePath">Chemin de l'image système.</param>
    /// <param name="gameCount">Nombre de jeux.</param>
    /// <param name="onFilterChanged">Callback appelé quand IsChecked change.</param>
    /// <param name="initialIsChecked">initialIsChecked permet d'initialiser la case à cocher directement
    /// à la construction, évitant un flash visuel lors d'un rechargement avec filtres préservés.</param>
    public SystemItemViewModel(string name, string? imagePath, int gameCount, Action onFilterChanged,
        bool initialIsChecked = true)
    {
        Name      = name;
        ImagePath = imagePath;
        GameCount = gameCount;
        _onFilterChanged = onFilterChanged;
        _isChecked = initialIsChecked;
    }

    /// <summary>
    /// Modifie IsChecked sans déclencher le callback de filtrage.
    /// Utilisé par les commandes "Tout cocher / Tout décocher" pour éviter
    /// N appels à RefreshFilter — le ViewModel appelant RefreshFilter une seule fois après.
    /// </summary>
    internal void SetCheckedSilent(bool value)
        => SetProperty(ref _isChecked, value, nameof(IsChecked));
}
