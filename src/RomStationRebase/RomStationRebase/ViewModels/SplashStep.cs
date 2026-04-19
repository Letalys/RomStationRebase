namespace RomStationRebase.ViewModels;

/// <summary>Statut visuel d'une étape dans la fenêtre de démarrage.</summary>
public enum SplashStepStatus
{
    Pending,
    Running,
    Success,
    Warning,
    Error
}

/// <summary>Représente une étape de la séquence de démarrage, avec son libellé et son statut.</summary>
public class SplashStep : ViewModelBase
{
    private SplashStepStatus _status = SplashStepStatus.Pending;
    private string? _detail;

    /// <summary>Libellé fixe de l'étape (ex : "Detecting RomStation").</summary>
    public string Label { get; }

    /// <summary>Statut courant — contrôle l'icône et l'animation dans le DataTemplate.</summary>
    public SplashStepStatus Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    /// <summary>Détail optionnel affiché sous le libellé (chemin trouvé, message d'erreur…).</summary>
    public string? Detail
    {
        get => _detail;
        set => SetProperty(ref _detail, value);
    }

    public SplashStep(string label) => Label = label;
}
