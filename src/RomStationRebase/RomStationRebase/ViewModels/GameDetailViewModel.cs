using System.Diagnostics;
using RomStationRebase.Models;
using RomStationRebase.Resources;

namespace RomStationRebase.ViewModels;

/// <summary>ViewModel de GameDetailWindow — expose les données d'un jeu sélectionné et les commandes d'action.</summary>
public class GameDetailViewModel : ViewModelBase
{
    private readonly string _romStationPath;

    public string   Title               { get; }
    public string   DisplayYear         { get; }
    public string?  DeveloperName       { get; }
    public string?  PublisherName       { get; }
    public string?  DisplayPlayers      { get; }
    public string   SystemName          { get; }
    public string?  SystemImagePath     { get; }
    public string?  CoverPath           { get; }
    public bool     CoverExists         { get; }
    public string   Directory           { get; }
    public string?  Description         { get; }
    public bool     DescriptionIsFallback { get; }
    public string   GenresDisplay       { get; }
    public bool     GenresHasFallback   { get; }
    public IReadOnlyList<GameLanguageInfo> Languages { get; }
    public bool     HasLanguages        { get; }
    public string?  RomStationUrl       { get; }
    public string   FallbackTooltip     { get; }

    // ── Commandes d'action ────────────────────────────────────────────────

    public RelayCommand OpenRomStationCommand { get; }
    public RelayCommand OpenGameFolderCommand { get; }

    /// <summary>Callback injecté par GameDetailWindow pour afficher le dialog "dossier introuvable" avec l'Owner correct.</summary>
    public Action? ShowFolderNotFoundDialog { get; set; }

    public GameDetailViewModel(GameDetail detail, string romStationPath)
    {
        _romStationPath      = romStationPath;
        Title                = detail.Title;
        DisplayYear          = detail.Year?.ToString() ?? "—";
        DeveloperName        = detail.DeveloperName;
        PublisherName        = detail.PublisherName;
        DisplayPlayers       = detail.Players?.ToString();
        SystemName           = detail.SystemName;
        SystemImagePath      = detail.SystemImagePath;
        CoverPath            = detail.CoverPath;
        CoverExists          = detail.CoverExists;
        Directory            = detail.Directory;
        Description          = detail.Description;
        DescriptionIsFallback = detail.DescriptionIsFallback;
        GenresDisplay        = detail.Genres.Count > 0
            ? string.Join(", ", detail.Genres)
            : "—";
        GenresHasFallback    = detail.GenresHasFallback;
        Languages            = detail.Languages;
        HasLanguages         = detail.Languages.Count > 0;
        RomStationUrl        = detail.RomStationUrl;
        FallbackTooltip      = detail.RequestedLocale == "fr"
            ? Strings.GameDetail_FallbackTooltip_Fr
            : Strings.GameDetail_FallbackTooltip_En;

        OpenRomStationCommand = new RelayCommand(
            execute:    OnOpenRomStation,
            canExecute: () => !string.IsNullOrWhiteSpace(RomStationUrl));

        OpenGameFolderCommand = new RelayCommand(OnOpenGameFolder);
    }

    private void OnOpenRomStation()
    {
        if (string.IsNullOrWhiteSpace(RomStationUrl)) return;
        Process.Start(new ProcessStartInfo { FileName = RomStationUrl, UseShellExecute = true });
    }

    private void OnOpenGameFolder()
    {
        var absolutePath = System.IO.Path.Combine(_romStationPath, "app", Directory);
        if (System.IO.Directory.Exists(absolutePath))
            Process.Start(new ProcessStartInfo { FileName = absolutePath, UseShellExecute = true });
        else
            ShowFolderNotFoundDialog?.Invoke();
    }
}
