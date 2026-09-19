using System.IO;
using System.Reflection;
using RomStationRebase.Models;
using RomStationRebase.Services;

namespace RomStationRebase.ViewModels;

/// <summary>Règles basculées pour un seul jeu dans le tableau du rebase (null : la valeur de l'architecture).</summary>
public readonly record struct GameRuleOverrides(bool? KeepFileName, bool? M3U, bool? Extract, string? Transform = null)
{
    public bool IsEmpty => KeepFileName is null && M3U is null && Extract is null && Transform is null;
}

/// <summary>
/// Sélection de travail partagée entre la fenêtre principale (les jeux cochés) et la fenêtre de rebase
/// (les paramètres et les règles par jeu). Elle sait de quel fichier elle vient et si elle en diffère :
/// c'est ce qui donne leur sens à « Enregistrer » et à l'avertissement avant de quitter.
/// </summary>
public sealed class RebasePresetSessionViewModel : ViewModelBase
{
    private readonly Func<IReadOnlyList<GameItemViewModel>> _selectedGames;
    private readonly Func<RebasePresetSettings>                _defaultSettings;

    private string? _filePath;
    private string  _savedFingerprint = string.Empty;
    private bool    _isDirty;

    /// <param name="selectedGames">Jeux cochés dans la bibliothèque, à l'instant de l'appel.</param>
    /// <param name="defaultSettings">Paramètres à enregistrer tant que la fenêtre de rebase n'en a pas fourni : les derniers utilisés.</param>
    public RebasePresetSessionViewModel(Func<IReadOnlyList<GameItemViewModel>> selectedGames, Func<RebasePresetSettings> defaultSettings)
    {
        _selectedGames   = selectedGames;
        _defaultSettings = defaultSettings;
    }

    /// <summary>Paramètres du rebase venus du fichier ou de la fenêtre de rebase. Null : ceux des préférences s'appliquent.</summary>
    public RebasePresetSettings? Settings { get; private set; }

    /// <summary>Règles par jeu, par identifiant RomStation.</summary>
    public Dictionary<int, GameRuleOverrides> Overrides { get; } = [];

    public string? FilePath
    {
        get => _filePath;
        private set
        {
            if (!SetProperty(ref _filePath, value)) return;
            OnPropertyChanged(nameof(HasFile));
            OnPropertyChanged(nameof(FileName));
            OnPropertyChanged(nameof(DisplayText));
        }
    }

    public bool    HasFile  => _filePath is not null;
    public string? FileName => _filePath is null ? null : Path.GetFileName(_filePath);

    /// <summary>True si la sélection, les paramètres ou les règles par jeu diffèrent du fichier ouvert.</summary>
    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (SetProperty(ref _isDirty, value))
                OnPropertyChanged(nameof(DisplayText));
        }
    }

    /// <summary>Nom du fichier suivi d'un point quand il reste des modifications à enregistrer.</summary>
    public string DisplayText => _filePath is null ? string.Empty : FileName + (_isDirty ? " •" : string.Empty);

    // ── Cycle de vie ──────────────────────────────────────────────────────

    /// <summary>
    /// Adopte un fichier qui vient d'être ouvert et appliqué à la bibliothèque. L'état de référence est
    /// celui qui a pu être appliqué : des jeux disparus ne rendent pas la sélection « modifiée ».
    /// </summary>
    public void Adopt(string path, RebasePresetSettings settings, IReadOnlyDictionary<int, GameRuleOverrides> overridesByRid)
    {
        Settings = settings.Clone();
        Overrides.Clear();
        foreach (var (rid, o) in overridesByRid)
            if (!o.IsEmpty) Overrides[rid] = o;
        FilePath = path;
        MarkSaved();
    }

    /// <summary>Oublie le fichier courant. Les jeux cochés ne sont pas touchés : c'est l'appelant qui décide.</summary>
    public void Close()
    {
        Settings = null;
        Overrides.Clear();
        FilePath = null;
        _savedFingerprint = string.Empty;
        IsDirty = false;
    }

    /// <summary>La fenêtre de rebase rend ses paramètres et les règles par jeu, à sa fermeture ou avant un enregistrement.</summary>
    public void Capture(RebasePresetSettings settings, IReadOnlyDictionary<int, GameRuleOverrides> overridesByRid)
    {
        Settings = settings.Clone();
        Overrides.Clear();
        foreach (var (rid, o) in overridesByRid)
            if (!o.IsEmpty) Overrides[rid] = o;
        RefreshDirty();
    }

    /// <summary>A rappeler quand les jeux cochés changent.</summary>
    public void RefreshDirty()
        => IsDirty = HasFile && Fingerprint() != _savedFingerprint;

    // ── Fichier ───────────────────────────────────────────────────────────

    /// <summary>Contenu à écrire : les jeux cochés, triés pour un fichier stable et relisible.</summary>
    public RebasePreset BuildFile()
    {
        var file = new RebasePreset
        {
            AppVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3),
            SavedAt    = DateTime.Now,
            Settings   = (Settings ?? _defaultSettings()).Clone(),
        };

        foreach (var g in _selectedGames()
                     .OrderBy(g => g.SystemName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(g => g.Title, StringComparer.OrdinalIgnoreCase))
        {
            Overrides.TryGetValue(g.Rid, out var o);
            file.Games.Add(new RebasePresetGame
            {
                Rid = g.Rid, Title = g.Title, System = g.SystemName,
                KeepFileName = o.KeepFileName, M3U = o.M3U, Extract = o.Extract, Transform = o.Transform,
            });
        }
        return file;
    }

    /// <summary>Écrit la sélection dans le fichier donné, qui devient le fichier courant. Les erreurs disque remontent à l'appelant.</summary>
    public void SaveTo(string path)
    {
        RebasePresetService.Save(path, BuildFile());
        FilePath = path;
        MarkSaved();
    }

    /// <summary>Dossier proposé par les boîtes Ouvrir et Enregistrer, mémorisé par la fenêtre principale dans les préférences.</summary>
    public string? LastDirectory { get; set; }

    /// <summary>Déclenché après un enregistrement ou une ouverture réussis, avec le dossier à mémoriser.</summary>
    public event Action<string>? DirectoryUsed;

    internal void NotifyDirectoryUsed(string filePath)
    {
        string? dir = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(dir)) return;
        LastDirectory = dir;
        DirectoryUsed?.Invoke(dir);
    }

    /// <summary>
    /// Enregistre dans le fichier courant, ou demande un nom s'il n'y en a pas ou si « Enregistrer sous » est demandé.
    /// Commun aux deux fenêtres. Retourne false si l'utilisateur renonce ou si l'écriture échoue (il en est alors averti).
    /// </summary>
    public bool SaveInteractive(System.Windows.Window? owner, bool saveAs)
    {
        string? path = !saveAs && HasFile ? FilePath : AskSavePath(owner);
        if (path is null) return false;

        try
        {
            SaveTo(path);
            NotifyDirectoryUsed(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            new Views.Dialogs.ConfirmDialog(
                Resources.Strings.Preset_SaveError_Title,
                string.Format(Resources.Strings.Preset_SaveError_Message, path, ex.Message),
                "OK") { Owner = owner }.ShowDialog();
            return false;
        }
    }

    private string? AskSavePath(System.Windows.Window? owner)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title           = Resources.Strings.Preset_SaveAs_DialogTitle,
            Filter          = FileDialogFilter,
            FileName        = FileName ?? Resources.Strings.Preset_DefaultFileName + RebasePresetService.Extension,
            DefaultExt      = RebasePresetService.Extension,
            AddExtension    = true,
            OverwritePrompt = true,
        };
        string? dir = HasFile ? Path.GetDirectoryName(FilePath) : LastDirectory;
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            dialog.InitialDirectory = dir;

        if (dialog.ShowDialog(owner) != true) return null;

        // La boîte n'a vérifié l'écrasement que pour le nom saisi : si l'extension ajoutée désigne un autre fichier, on redemande
        string path = RebasePresetService.EnsureExtension(dialog.FileName);
        if (!string.Equals(path, dialog.FileName, StringComparison.OrdinalIgnoreCase) && File.Exists(path))
        {
            var confirm = new Views.Dialogs.ConfirmDialog(
                Resources.Strings.Preset_Overwrite_Title,
                string.Format(Resources.Strings.Preset_Overwrite_Message, Path.GetFileName(path)),
                Resources.Strings.Preset_Overwrite_Proceed,
                Resources.Strings.Common_Cancel) { Owner = owner };
            confirm.ShowDialog();
            if (!confirm.Result) return null;
        }
        return path;
    }

    /// <summary>Filtre commun aux boîtes Ouvrir et Enregistrer.</summary>
    public static string FileDialogFilter
        => $"{Resources.Strings.Preset_FileFilter} (*{RebasePresetService.Extension})|*{RebasePresetService.Extension};*{RebasePresetService.LegacyExtension}|{Resources.Strings.Preset_FileFilter_All} (*.*)|*.*";

    private void MarkSaved()
    {
        _savedFingerprint = Fingerprint();
        IsDirty = false;
    }

    /// <summary>Empreinte de ce qui serait écrit, hors date et version : deux états égaux donnent la même chaîne.</summary>
    private string Fingerprint()
    {
        var file = BuildFile();
        file.SavedAt    = default;
        file.AppVersion = null;
        return RebasePresetService.Serialize(file);
    }
}
