using RomStationRebase.Helpers;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using RomStationRebase.Models;
using RomStationRebase.Resources;
using RomStationRebase.Services;
using RomStationRebase.Views.Dialogs;

namespace RomStationRebase.ViewModels;

/// <summary>
/// Fenêtre des outils externes de conversion : liste des descripteurs du dossier utilisateur, fiche de l'outil
/// sélectionné, emplacement de son exécutable. RSR ne distribue aucun de ces programmes et n'en lance aucun
/// qu'il aurait trouvé seul : l'utilisateur indique le sien (Parcourir, ou adoption explicite d'une proposition).
/// Même moule que l'éditeur d'architectures : brouillons, écriture à Enregistrer, suppression immédiate.
/// </summary>
public class ExternalToolsViewModel : ViewModelBase
{
    private readonly ExternalToolService _service = new();
    private readonly string? _romStationPath;
    private readonly IReadOnlyList<string> _emulatorDirectories;
    private ExternalToolDraftViewModel?  _selected;

    /// <summary>
    /// Emulateurs installés par RomStation, lus dans la copie de sa base : leurs paquets contiennent souvent les outils
    /// (MAME : chdman, Dolphin : DolphinTool). Base illisible → liste vide, la détection se rabat sur l'arborescence du disque.
    /// </summary>
    private static (string? RomStationPath, IReadOnlyList<string> EmulatorDirectories) ReadRomStation()
    {
        try
        {
            var state = new ConfigService().LoadAppState();
            IReadOnlyList<string> directories = [];
            try
            {
                if (!string.IsNullOrWhiteSpace(state.DatabaseCopyPath))
                    directories = new DerbyService().GetEmulatorDirectories(state.DatabaseCopyPath);
            }
            catch { /* table absente d'une ancienne base, copie verrouillée : l'arborescence du disque suffira */ }
            return (state.RomStationPath, directories);
        }
        catch { return (null, []); }
    }

    public ObservableCollection<ExternalToolDraftViewModel> Tools { get; } = new();

    /// <summary>True si au moins une écriture a eu lieu — l'appelant relit alors ses outils.</summary>
    public bool Saved { get; private set; }

    /// <summary>Fenêtre propriétaire — définie par la View pour les dialogues.</summary>
    public Window? OwnerWindow { get; set; }

    /// <summary>Ferme la fenêtre — injecté par la View, qui demande confirmation si des brouillons sont modifiés.</summary>
    public Action? CloseWindow { get; set; }

    public ExternalToolDraftViewModel? Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value)) return;
            OnPropertyChanged(nameof(HasSelection));
            if (value?.Origin == ArchitectureOrigin.Custom) ShowAdvanced = true;
        }
    }

    public bool HasSelection => _selected is not null;

    // ── Réglages avancés ──────────────────────────────────────────────────
    // Un outil par défaut ne demande qu'une chose : son exécutable. Sa ligne de commande reste repliée,
    // sauf pour un outil personnalisé, qu'il faut bien décrire.

    private bool _showAdvanced;

    public bool ShowAdvanced
    {
        get => _showAdvanced;
        set { if (SetProperty(ref _showAdvanced, value)) OnPropertyChanged(nameof(AdvancedToggleText)); }
    }

    public string AdvancedToggleText => _showAdvanced ? Strings.Tools_Advanced_Hide : Strings.Tools_Advanced_Show;

    public ICommand ToggleAdvancedCommand { get; private set; } = null!;
    public bool HasChanges   => Tools.Any(t => t.IsDirty);

    public ICommand AddCommand             { get; }
    public ICommand DuplicateCommand       { get; }
    public ICommand DeleteCommand          { get; }
    public ICommand RestoreDefaultsCommand { get; }
    public ICommand OpenFolderCommand      { get; }
    public ICommand BrowseCommand          { get; }
    public ICommand UseDetectedCommand     { get; }
    public ICommand ClearPathCommand       { get; }
    public ICommand TestCommand            { get; }
    public ICommand SaveCommand            { get; }
    public ICommand CancelCommand          { get; }

    public ExternalToolsViewModel()
    {
        AddCommand             = new RelayCommand(AddNew);
        DuplicateCommand       = new RelayCommand(Duplicate,   () => _selected is not null);
        DeleteCommand          = new RelayCommand(Delete,      () => _selected is not null);
        RestoreDefaultsCommand = new RelayCommand(RestoreDefaults);
        OpenFolderCommand      = new RelayCommand(OpenFolder);
        BrowseCommand          = new RelayCommand(Browse,      () => _selected is not null);
        UseDetectedCommand     = new RelayCommand(UseDetected, () => _selected?.ShowDetected == true);
        ClearPathCommand       = new RelayCommand(() => { if (_selected is not null) _selected.ExecutablePath = string.Empty; },
                                                  () => !string.IsNullOrEmpty(_selected?.ExecutablePath));
        TestCommand            = new RelayCommand(async () => await TestAsync(), () => _selected is { IsAvailable: true, IsTesting: false });
        SaveCommand            = new RelayCommand(Save);
        CancelCommand          = new RelayCommand(() => CloseWindow?.Invoke());
        ToggleAdvancedCommand  = new RelayCommand(() => ShowAdvanced = !ShowAdvanced);

        (_romStationPath, _emulatorDirectories) = ReadRomStation();
        Load(null);
    }

    // ── Chargement ────────────────────────────────────────────────────────

    private void Load(string? selectId)
    {
        foreach (var draft in Tools.ToList()) Detach(draft);

        Dictionary<string, string> paths;
        List<ExternalTool> tools;
        try
        {
            tools = _service.LoadTools();
            paths = _service.LoadExecutablePaths();
        }
        catch
        {
            tools = [];
            paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        foreach (var tool in tools)
        {
            paths.TryGetValue(tool.Id, out string? path);
            // Emulateur mis à jour par RomStation depuis le dernier passage : on montre l'emplacement réellement utilisé
            if (!string.IsNullOrEmpty(path) && !File.Exists(path))
                path = ExternalToolService.FollowEmulatorUpdate(path) ?? path;
            var draft = new ExternalToolDraftViewModel(tool, path, isPersisted: true);
            Attach(draft);
        }

        Selected = Tools.FirstOrDefault(t => string.Equals(t.Id, selectId, StringComparison.OrdinalIgnoreCase))
                   ?? Tools.FirstOrDefault();
    }

    private void Attach(ExternalToolDraftViewModel draft)
    {
        draft.DirtyChanged      += OnDirtyChanged;
        draft.ExecutableChanged += OnExecutableChanged;
        RefreshDetected(draft);
        Tools.Add(draft);
    }

    private void Detach(ExternalToolDraftViewModel draft)
    {
        draft.DirtyChanged      -= OnDirtyChanged;
        draft.ExecutableChanged -= OnExecutableChanged;
        Tools.Remove(draft);
    }

    private void OnDirtyChanged() => OnPropertyChanged(nameof(HasChanges));

    /// <summary>
    /// Chaque outil a son emplacement propre, même quand plusieurs partagent un programme (les trois chdman) :
    /// régler l'un ne touche jamais aux autres — retour du dev, le réglage en cascade prêtait à confusion.
    /// </summary>
    private void OnExecutableChanged(ExternalToolDraftViewModel source) => RefreshDetected(source);

    private void RefreshDetected(ExternalToolDraftViewModel draft)
    {
        try   { draft.DetectedPath = ExternalToolService.Detect(draft.ToModel(), _romStationPath, _emulatorDirectories); }
        catch { draft.DetectedPath = null; }
    }

    // ── Ajouter / Dupliquer / Supprimer / Restaurer ───────────────────────

    private void AddNew()
    {
        var tool = new ExternalTool
        {
            Id     = ExternalToolService.MakeId(Strings.Tools_NewLabel, Tools.Select(t => t.Id)),
            Label  = Strings.Tools_NewLabel,
            Arguments = ["{input}", "{output}"],
            Origin = ArchitectureOrigin.Custom,
        };
        var draft = new ExternalToolDraftViewModel(tool, null, isPersisted: false);
        Attach(draft);
        Selected = draft;
        OnDirtyChanged();
    }

    private void Duplicate()
    {
        if (_selected is null) return;
        string baseLabel = _selected.Label.Trim();
        if (baseLabel.EndsWith("(Default)", StringComparison.OrdinalIgnoreCase))
            baseLabel = baseLabel[..^"(Default)".Length].TrimEnd();

        var clone = _selected.ToModel();
        clone.Id     = ExternalToolService.MakeId(baseLabel, Tools.Select(t => t.Id));
        clone.Label  = string.Format(Strings.ArchEditor_CopyLabel, baseLabel);
        clone.Origin = ArchitectureOrigin.Custom;

        var draft = new ExternalToolDraftViewModel(clone, _selected.ExecutablePath, isPersisted: false);
        Attach(draft);
        Selected = draft;
        OnDirtyChanged();
    }

    /// <summary>Efface l'outil du dossier utilisateur, défaut compris. Une architecture qui le désigne copiera simplement sans convertir.</summary>
    private void Delete()
    {
        var draft = _selected;
        if (draft is null) return;
        if (!ShowConfirm(Strings.Tools_Delete_Title, string.Format(Strings.Tools_Delete_Message, draft.DisplayName),
                Strings.ArchEditor_Delete, Strings.General_Cancel).Result)
            return;

        if (draft.IsPersisted)
        {
            try { _service.Delete(draft.Id); Saved = true; }
            catch (Exception ex)
            {
                ShowConfirm(Strings.ArchEditor_Error_Title, ErrorCodes.Tag(string.Format(Strings.ArchEditor_Error_Write, draft.DisplayName, ex.Message), ErrorCodes.ToolWriteFailed), "OK");
                return;
            }
        }

        int index = Tools.IndexOf(draft);
        Detach(draft);
        Selected = Tools.Count > 0 ? Tools[Math.Min(index, Tools.Count - 1)] : null;
        OnDirtyChanged();
    }

    private void RestoreDefaults()
    {
        string message = Strings.Tools_Restore_Message;
        if (HasChanges) message += Environment.NewLine + Environment.NewLine + Strings.ArchEditor_Discard_Message;
        if (!ShowConfirm(Strings.Tools_Restore, message, Strings.Tools_Restore, Strings.General_Cancel).Result)
            return;

        try { _service.RestoreDefaults(); Saved = true; }
        catch (Exception ex)
        {
            ShowConfirm(Strings.ArchEditor_Error_Title, ErrorCodes.Tag(string.Format(Strings.ArchEditor_Error_Write, Strings.Tools_Restore, ex.Message), ErrorCodes.ToolWriteFailed), "OK");
            return;
        }
        Load(_selected?.Id);
        OnDirtyChanged();
    }

    // ── Exécutable ────────────────────────────────────────────────────────

    private void Browse()
    {
        if (_selected is null) return;
        var dialog = new OpenFileDialog
        {
            Title           = string.Format(Strings.Tools_Browse_Title, _selected.Executable),
            Filter          = Strings.Tools_Browse_Filter,
            CheckFileExists = true,
        };
        try
        {
            string? start = !string.IsNullOrEmpty(_selected.ExecutablePath) ? Path.GetDirectoryName(_selected.ExecutablePath)
                          : _selected.DetectedPath is not null              ? Path.GetDirectoryName(_selected.DetectedPath)
                          : null;
            if (start is not null && Directory.Exists(start)) dialog.InitialDirectory = start;
        }
        catch (ArgumentException) { /* chemin saisi invalide : le dialogue s'ouvre ailleurs */ }

        if (dialog.ShowDialog(OwnerWindow) == true)
            _selected.ExecutablePath = dialog.FileName;
    }

    private void UseDetected()
    {
        if (_selected?.DetectedPath is { } path) _selected.ExecutablePath = path;
    }

    /// <summary>Lance l'outil avec ses arguments de version : vérifie qu'il répond et qu'il est assez récent.</summary>
    private async Task TestAsync()
    {
        var draft = _selected;
        if (draft is null || !draft.IsAvailable) return;

        draft.IsTesting = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            var result = await Task.Run(() => ExternalToolService.TestAsync(draft.ToModel(), draft.ExecutablePath));
            draft.SetTestResult(result);
        }
        catch (Exception ex)
        {
            draft.SetTestResult(new ExternalToolTestResult(false, null, ex.Message));
        }
        finally
        {
            draft.IsTesting = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    // ── Enregistrer / Fermer ──────────────────────────────────────────────

    private void Save()
    {
        var dirty  = Tools.Where(t => t.IsDirty).ToList();
        var errors = dirty.SelectMany(t => t.Validate()).ToList();
        if (errors.Count > 0)
        {
            ShowConfirm(Strings.ArchEditor_Validation_Title, string.Join(Environment.NewLine, errors), "OK");
            return;
        }

        foreach (var draft in dirty)
        {
            try
            {
                var model = draft.ToModel();
                _service.Save(model);
                _service.SetExecutablePath(model.Id, draft.ExecutablePath);
                draft.MarkSaved();
                Saved = true;
            }
            catch (Exception ex)
            {
                ShowConfirm(Strings.ArchEditor_Validation_Title,
                    ErrorCodes.Tag(string.Format(Strings.ArchEditor_Error_Write, draft.DisplayName, ex.Message), ErrorCodes.ToolWriteFailed), "OK");
                return;
            }
        }
        CloseWindow?.Invoke();
    }

    /// <summary>Appelé par la View avant fermeture : true si rien n'est modifié ou si l'utilisateur abandonne ses changements.</summary>
    public bool ConfirmClose()
    {
        if (!HasChanges) return true;
        return ShowConfirm(Strings.ArchEditor_Discard_Title, Strings.ArchEditor_Discard_Message,
            Strings.ArchEditor_Discard_Confirm, Strings.General_Cancel).Result;
    }

    private void OpenFolder()
    {
        try
        {
            string path = ExternalToolService.UserToolsDirectory;
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch
        {
            // Explorateur indisponible : rien à faire de plus
        }
    }

    private ConfirmDialog ShowConfirm(string title, string message, string primary, string? secondary = null)
    {
        var dlg = new ConfirmDialog(title, message, primary, secondary) { Owner = OwnerWindow };
        dlg.ShowDialog();
        return dlg;
    }
}
