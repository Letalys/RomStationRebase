using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using RomStationRebase.Resources;
using RomStationRebase.Services;

namespace RomStationRebase.ViewModels;

/// <summary>
/// Fenêtre du journal de rebase : relit le fichier deux fois par seconde et ajoute les nouvelles lignes,
/// qu'il soit en cours d'écriture ou terminé depuis longtemps. Ne dépend pas de la fenêtre de rebase :
/// tout passe par le fichier.
/// </summary>
public class RebaseLogViewModel : ViewModelBase
{
    private readonly DispatcherTimer _timer;
    private RebaseLogTailReader _reader;
    private string _filterText = string.Empty;
    private bool   _problemsOnly;
    private bool   _follow = true;
    private int    _warningCount;
    private int    _errorCount;

    /// <summary>Toutes les lignes lues, dans l'ordre du fichier.</summary>
    public ObservableCollection<RebaseLogLine> Lines { get; } = new();

    /// <summary>Vue filtrée affichée par la grille.</summary>
    public ICollectionView LinesView { get; }

    /// <summary>Fait défiler la grille jusqu'à la dernière ligne — injecté par la View.</summary>
    public Action? ScrollToEnd { get; set; }

    public ICommand OpenFileCommand   { get; }
    public ICommand OpenFolderCommand { get; }

    public RebaseLogViewModel(string filePath)
    {
        _reader   = new RebaseLogTailReader(filePath);
        LinesView = CollectionViewSource.GetDefaultView(Lines);
        LinesView.Filter = o => o is RebaseLogLine line && Matches(line);

        OpenFileCommand   = new RelayCommand(OpenFile);
        OpenFolderCommand = new RelayCommand(OpenFolder);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => Poll();
    }

    // ── État ──────────────────────────────────────────────────────────────

    public string FilePath => _reader.FilePath;
    public string FileName => Path.GetFileName(_reader.FilePath);

    /// <summary>Titre de la fenêtre : « Journal du rebase · RSR_20260920_101502.log ».</summary>
    public string WindowTitle => $"{Strings.LogViewer_Title} · {FileName}";

    public string FilterText
    {
        get => _filterText;
        set { if (SetProperty(ref _filterText, value ?? string.Empty)) RefreshView(); }
    }

    /// <summary>N'affiche que les avertissements et les erreurs.</summary>
    public bool ProblemsOnly
    {
        get => _problemsOnly;
        set { if (SetProperty(ref _problemsOnly, value)) RefreshView(); }
    }

    /// <summary>Suit la fin du fichier : chaque ajout fait défiler jusqu'à la dernière ligne.</summary>
    public bool Follow
    {
        get => _follow;
        set { if (SetProperty(ref _follow, value) && value) ScrollToEnd?.Invoke(); }
    }

    public string StatusText => string.Format(Strings.LogViewer_Status, Lines.Count, _warningCount, _errorCount);

    // ── Lecture ───────────────────────────────────────────────────────────

    /// <summary>Première lecture puis suivi. Appelé par la View une fois affichée.</summary>
    public void Start()
    {
        Poll();
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    /// <summary>Affiche un autre journal dans la même fenêtre.</summary>
    public void Load(string filePath)
    {
        if (string.Equals(filePath, _reader.FilePath, StringComparison.OrdinalIgnoreCase)) return;
        _reader = new RebaseLogTailReader(filePath);
        Clear();
        OnPropertyChanged(nameof(FilePath));
        OnPropertyChanged(nameof(FileName));
        OnPropertyChanged(nameof(WindowTitle));
        Poll();
    }

    private void Clear()
    {
        Lines.Clear();
        _warningCount = 0;
        _errorCount   = 0;
        OnPropertyChanged(nameof(StatusText));
    }

    internal void Poll()
    {
        var added = _reader.ReadNew();
        if (_reader.WasReset) Clear();
        if (added.Count == 0) return;

        foreach (var line in added)
        {
            Lines.Add(line);
            if (line.Level == RebaseLogLevel.Warning) _warningCount++;
            else if (line.Level == RebaseLogLevel.Error) _errorCount++;
        }
        OnPropertyChanged(nameof(StatusText));
        if (_follow) ScrollToEnd?.Invoke();
    }

    private bool Matches(RebaseLogLine line)
        => (!_problemsOnly || line.IsProblem)
        && (_filterText.Length == 0 || line.Message.Contains(_filterText, StringComparison.CurrentCultureIgnoreCase));

    private void RefreshView()
    {
        LinesView.Refresh();
        if (_follow) ScrollToEnd?.Invoke();
    }

    // ── Commandes ─────────────────────────────────────────────────────────

    private void OpenFile()
    {
        var dialog = new OpenFileDialog
        {
            Title  = Strings.LogViewer_Title,
            Filter = $"{Strings.LogViewer_Title} (*.log)|*.log|*.*|*.*",
        };
        string? dir = Path.GetDirectoryName(_reader.FilePath);
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) dialog.InitialDirectory = dir;
        if (dialog.ShowDialog() == true) Load(dialog.FileName);
    }

    private void OpenFolder()
    {
        try
        {
            // Le fichier est sélectionné dans l'Explorateur quand il existe encore
            if (File.Exists(_reader.FilePath))
                Process.Start("explorer.exe", $"/select,\"{_reader.FilePath}\"");
            else if (Path.GetDirectoryName(_reader.FilePath) is { Length: > 0 } dir && Directory.Exists(dir))
                Process.Start("explorer.exe", dir);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { /* l'Explorateur ne s'ouvre pas : rien à signaler */ }
    }
}
