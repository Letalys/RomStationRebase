using System.Windows;
using System.Windows.Input;
using RomStationRebase.Models;
using RomStationRebase.Resources;
using RomStationRebase.Services;

namespace RomStationRebase.ViewModels;

/// <summary>
/// ViewModel de la fenêtre Paramètres.
/// Travaille sur une COPIE des UserPreferences — annulable via le bouton Annuler.
/// Les modifications sont persistées uniquement si l'utilisateur clique sur Enregistrer.
/// </summary>
public class SettingsViewModel : ViewModelBase
{
    private readonly UserPreferences _originalPrefs;
    private readonly ConfigService   _config = new();

    // Copie de travail — modifications annulables
    private string _selectedLanguage;
    private string _selectedTheme;

    // Valeur d'origine pour détecter un changement de langue (→ message redémarrage)
    private readonly string _initialLanguage;

    // Chemin RomStation lu depuis AppState pour le bouton "Ouvrir dossier RomStation"
    private readonly string _romStationPath;

    // Métadonnées distribuées (repo, auteur, licence)
    private readonly AppMetadata _metadata;

    public SettingsViewModel(UserPreferences preferences)
    {
        _originalPrefs = preferences;

        // Initialiser la copie de travail depuis les préférences actuelles
        _selectedLanguage = preferences.AppLanguage;
        _selectedTheme    = preferences.Theme;
        _initialLanguage  = preferences.AppLanguage;

        SaveCommand   = new RelayCommand<Window>(Save);
        CancelCommand = new RelayCommand<Window>(Cancel);

        // Chemin RomStation — lu depuis AppState, peut être vide si RSR n'a jamais détecté RomStation
        try
        {
            var appState    = _config.LoadAppState();
            _romStationPath = appState?.RomStationPath ?? string.Empty;
        }
        catch
        {
            _romStationPath = string.Empty;
        }

        OpenInstallFolderCommand    = new RelayCommand(OpenInstallFolder);
        OpenUserDataFolderCommand   = new RelayCommand(OpenUserDataFolder);
        OpenRomStationFolderCommand = new RelayCommand(OpenRomStationFolder, () => IsRomStationPathAvailable);

        // Chargement des métadonnées distribuées (fichier config/app-metadata.json)
        _metadata = _config.LoadAppMetadata();

        // Version de l'assembly, ex : "1.0.0.0" → "1.0.0"
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        string versionString = version != null
            ? $"{version.Major}.{version.Minor}.{version.Build}"
            : "?";

        AppVersionText = $"RomStation Rebase v{versionString}";
        CopyrightText  = string.Format(Strings.Settings_About_Copyright, _metadata.Author, _metadata.License);

        OpenRepositoryCommand = new RelayCommand(OpenRepository, () => IsRepositoryUrlAvailable);
        OpenWikiCommand       = new RelayCommand(OpenWiki,       () => IsWikiUrlAvailable);
    }

    /// <summary>Langue sélectionnée (valeurs : "auto", "fr", "en").</summary>
    public string SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (SetProperty(ref _selectedLanguage, value))
                OnPropertyChanged(nameof(ShowRestartMessage));
        }
    }

    /// <summary>Thème sélectionné (valeurs : "Light").</summary>
    public string SelectedTheme
    {
        get => _selectedTheme;
        set => SetProperty(ref _selectedTheme, value);
    }

    /// <summary>True si la langue a été modifiée par rapport à l'état d'ouverture du panneau.</summary>
    public bool ShowRestartMessage => _selectedLanguage != _initialLanguage;

    /// <summary>True si le chemin RomStation est connu (lu depuis AppState). Contrôle l'activation du bouton.</summary>
    public bool IsRomStationPathAvailable => !string.IsNullOrWhiteSpace(_romStationPath);

    /// <summary>Nom du produit et version, ex : "RomStation Rebase v1.0.0".</summary>
    public string AppVersionText { get; }

    /// <summary>Ligne de copyright formatée, ex : "© Letalys — Licence MIT".</summary>
    public string CopyrightText { get; }

    /// <summary>True si l'URL du dépôt est disponible. Contrôle l'activation du bouton GitHub.</summary>
    public bool IsRepositoryUrlAvailable => !string.IsNullOrWhiteSpace(_metadata.RepositoryUrl);

    /// <summary>True si l'URL du wiki est disponible. Contrôle l'activation du bouton Documentation.</summary>
    public bool IsWikiUrlAvailable => !string.IsNullOrWhiteSpace(_metadata.WikiUrl);

    public ICommand SaveCommand                 { get; }
    public ICommand CancelCommand               { get; }
    public ICommand OpenInstallFolderCommand    { get; }
    public ICommand OpenUserDataFolderCommand   { get; }
    public ICommand OpenRomStationFolderCommand { get; }
    public ICommand OpenRepositoryCommand       { get; }
    public ICommand OpenWikiCommand             { get; }

    /// <summary>Applique les modifications, sauvegarde sur disque, ferme la fenêtre.</summary>
    private void Save(Window? window)
    {
        _originalPrefs.AppLanguage = _selectedLanguage;
        _originalPrefs.Theme       = _selectedTheme;

        try
        {
            _config.SaveUserPreferences(_originalPrefs);
        }
        catch
        {
            // Sauvegarde silencieuse — ne bloque pas la fermeture
        }

        ThemeService.Apply(_selectedTheme);
        window?.Close();
    }

    /// <summary>Ignore les modifications et ferme la fenêtre.</summary>
    private void Cancel(Window? window) => window?.Close();

    /// <summary>Ouvre dans l'Explorateur Windows le dossier d'installation de RSR.</summary>
    private void OpenInstallFolder()
        => OpenFolderSafe(AppContext.BaseDirectory);

    /// <summary>Ouvre dans l'Explorateur Windows le dossier %LOCALAPPDATA%\RomStationRebase\.</summary>
    private void OpenUserDataFolder()
    {
        string path = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RomStationRebase");
        OpenFolderSafe(path);
    }

    /// <summary>Ouvre dans l'Explorateur Windows le dossier d'installation de RomStation (si détecté).</summary>
    private void OpenRomStationFolder()
        => OpenFolderSafe(_romStationPath);

    /// <summary>Ouvre l'URL du dépôt GitHub dans le navigateur par défaut.</summary>
    private void OpenRepository()
    {
        if (!IsRepositoryUrlAvailable) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName        = _metadata.RepositoryUrl,
                UseShellExecute = true,
            });
        }
        catch
        {
            // Silencieux si le navigateur ne peut pas être lancé
        }
    }

    /// <summary>Ouvre l'URL du wiki GitHub dans le navigateur par défaut.</summary>
    private void OpenWiki()
    {
        if (!IsWikiUrlAvailable) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName        = _metadata.WikiUrl,
                UseShellExecute = true,
            });
        }
        catch
        {
            // Silencieux si le navigateur ne peut pas être lancé
        }
    }

    /// <summary>Ouvre un dossier dans l'Explorateur Windows. Silencieux en cas d'erreur.</summary>
    private static void OpenFolderSafe(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            // Crée le dossier utilisateur s'il n'existe pas encore (cas du premier lancement)
            if (!System.IO.Directory.Exists(path))
                System.IO.Directory.CreateDirectory(path);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName        = path,
                UseShellExecute = true,
            });
        }
        catch
        {
            // On ne veut pas bloquer l'UI si Explorer ne démarre pas
        }
    }
}
