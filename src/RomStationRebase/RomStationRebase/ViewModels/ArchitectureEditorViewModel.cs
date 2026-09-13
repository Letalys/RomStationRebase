using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using RomStationRebase.Models;
using RomStationRebase.Resources;
using RomStationRebase.Services;
using RomStationRebase.Views.Dialogs;

namespace RomStationRebase.ViewModels;

/// <summary>
/// ViewModel de l'éditeur d'architectures cibles. Travaille sur des brouillons ; Enregistrer écrit
/// les architectures modifiées dans le dossier de l'utilisateur, Supprimer et Restaurer agissent
/// immédiatement sur le disque.
/// </summary>
public class ArchitectureEditorViewModel : ViewModelBase
{
    private readonly ArchitectureService _service = new();
    private ArchitectureDraftViewModel?  _selected;

    // Erreurs survenues avant l'affichage de la fenêtre : montrées au Loaded, quand OwnerWindow est visible
    private readonly List<string> _pendingErrors = new();
    private bool _isLoaded;

    // ── Collections et état ───────────────────────────────────────────────

    /// <summary>Architectures distribuées, surchargées et personnalisées, sous forme de brouillons.</summary>
    public ObservableCollection<ArchitectureDraftViewModel> Architectures { get; } = new();

    /// <summary>Noms de systèmes proposés dans la colonne Système — la saisie reste libre.</summary>
    public IReadOnlyList<string> SystemNames { get; }

    /// <summary>True si des suggestions existent : la colonne Système devient une liste éditable.</summary>
    public bool HasSystemNames => SystemNames.Count > 0;

    /// <summary>True si au moins une écriture (enregistrement, suppression, restauration) a eu lieu.</summary>
    public bool Saved { get; private set; }

    /// <summary>Fenêtre propriétaire — définie par la View pour les ConfirmDialog.</summary>
    public Window? OwnerWindow { get; set; }

    /// <summary>Ferme la fenêtre — injecté par la View. La View demande confirmation si des brouillons sont modifiés.</summary>
    public Action? CloseWindow { get; set; }

    /// <summary>Architecture affichée dans la fiche. Sa table des systèmes est chargée à la première sélection.</summary>
    public ArchitectureDraftViewModel? Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value)) return;
            if (value is not null) EnsureMappingsLoaded(value);
            OnPropertyChanged(nameof(HasSelection));
            RefreshDeleteState();
        }
    }

    public bool HasSelection => _selected is not null;

    /// <summary>True si un brouillon au moins diffère du disque.</summary>
    public bool HasChanges => Architectures.Any(a => a.IsDirty);

    /// <summary>Restaurer la version livrée : surcharge enregistrée, ou distribuée retouchée mais pas encore écrite.</summary>

    /// <summary>True si une distribuée est masquée ou surchargée sur disque — active "Restaurer les architectures livrées".</summary>
    public bool CanRestoreShipped { get; private set; }

    // ── Commandes ─────────────────────────────────────────────────────────

    /// <summary>Nouvelle architecture personnalisée vide.</summary>
    public ICommand AddCommand           { get; }
    public ICommand DuplicateCommand     { get; }

    /// <summary>Création : effacée. Distribuée ou surchargée : masquée, restaurable par RestoreShippedCommand.</summary>
    public ICommand DeleteCommand        { get; }

    /// <summary>Remet la sélection dans sa version livrée.</summary>

    /// <summary>Remet toutes les distribuées dans leur état livré (masquées et surcharges), garde les personnalisées.</summary>
    public ICommand RestoreShippedCommand { get; }
    public ICommand AddMappingCommand    { get; }

    /// <summary>Retire une ligne de la table des systèmes. Paramètre : SystemMappingRowViewModel.</summary>
    public ICommand RemoveMappingCommand { get; }

    public ICommand OpenFolderCommand    { get; }
    public ICommand SaveCommand          { get; }
    public ICommand CancelCommand        { get; }

    // ── Constructeur ──────────────────────────────────────────────────────

    /// <param name="systemNames">Noms de systèmes à proposer dans la colonne Système ; liste vide = saisie libre seule.</param>
    public ArchitectureEditorViewModel(IReadOnlyList<string> systemNames)
    {
        SystemNames = systemNames
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        AddCommand           = new RelayCommand(AddNew);
        DuplicateCommand     = new RelayCommand(Duplicate,      () => _selected is not null);
        DeleteCommand        = new RelayCommand(Delete,         () => _selected is not null);
        RestoreShippedCommand = new RelayCommand(RestoreShipped, () => CanRestoreShipped);
        AddMappingCommand    = new RelayCommand(AddMapping,     () => _selected is not null);
        RemoveMappingCommand = new RelayCommand(param => { if (param is SystemMappingRowViewModel row) RemoveMapping(row); });
        OpenFolderCommand    = new RelayCommand(OpenFolder);
        SaveCommand          = new RelayCommand(Save);
        CancelCommand        = new RelayCommand(() => CloseWindow?.Invoke());

        Load();
    }

    // ── Chargement ────────────────────────────────────────────────────────

    private void Load()
    {
        try
        {
            foreach (var entry in _service.LoadArchitectures())
                Attach(new ArchitectureDraftViewModel(entry));
        }
        catch (Exception ex)
        {
            ReportError(string.Format(Strings.ArchEditor_Error_Load, ex.Message));
        }
        RefreshRestoreShippedState();
        Selected = Architectures.FirstOrDefault();
    }

    /// <summary>Recharge tout depuis le disque, en gardant la sélection par identifiant si elle existe encore.</summary>
    private void ReloadAll()
    {
        string? currentId = _selected?.Id;
        foreach (var draft in Architectures.ToList()) Detach(draft);
        Load();
        var match = currentId is null ? null
            : Architectures.FirstOrDefault(a => string.Equals(a.Id, currentId, StringComparison.OrdinalIgnoreCase));
        if (match is not null) Selected = match;
    }

    /// <summary>Relit l'état disque des distribuées — à appeler après chaque écriture.</summary>
    private void RefreshRestoreShippedState()
    {
        try   { CanRestoreShipped = _service.HasDistributedChanges(); }
        catch { CanRestoreShipped = false; }
        OnPropertyChanged(nameof(CanRestoreShipped));
    }

    private void Attach(ArchitectureDraftViewModel draft)
    {
        draft.AllSystems    = SystemNames; // chaque ligne ne proposera que les systèmes encore libres
        draft.DirtyChanged += RefreshDeleteState;
        Architectures.Add(draft);
    }

    private void Detach(ArchitectureDraftViewModel draft)
    {
        draft.DirtyChanged -= RefreshDeleteState;
        Architectures.Remove(draft);
    }

    /// <summary>Charge la table des systèmes d'un brouillon si ce n'est pas déjà fait. En cas d'échec, table vide et message.</summary>
    private void EnsureMappingsLoaded(ArchitectureDraftViewModel draft)
    {
        if (draft.MappingsLoaded) return;
        try
        {
            draft.LoadMappings(_service.LoadFolderTreeMapping(draft.Entry.FolderTreeMapping));
        }
        catch (Exception ex)
        {
            draft.LoadMappings(new FolderTreeMapping());
            ReportError(string.Format(Strings.ArchEditor_Error_LoadMapping, draft.DisplayName, ex.Message));
        }
    }

    /// <summary>Appelé par la View une fois la fenêtre affichée : montre les erreurs survenues pendant la construction.</summary>
    public void ReportPendingErrors()
    {
        _isLoaded = true;
        foreach (var message in _pendingErrors)
            ShowConfirm(Strings.ArchEditor_Error_Title, message, "OK");
        _pendingErrors.Clear();
    }

    private void ReportError(string message)
    {
        if (_isLoaded) ShowConfirm(Strings.ArchEditor_Error_Title, message, "OK");
        else           _pendingErrors.Add(message);
    }

    private void RefreshDeleteState()
    {
        OnPropertyChanged(nameof(HasChanges));
    }

    // ── Ajouter / Dupliquer / Supprimer / Restaurer ───────────────────────

    /// <summary>Nouvelle architecture personnalisée vide, avec les conventions EmulationStation comme point de départ.</summary>
    private void AddNew()
    {
        string label = Strings.ArchEditor_NewLabel;
        var entry = new ArchitectureEntry
        {
            Id                = MakeUniqueId(ArchitectureService.MakeId(label)),
            Label             = label,
            CoverFolder       = "images",
            CoverSuffix       = "-image",
            GamelistFormat    = "emulationstation",
            Origin            = ArchitectureOrigin.Custom,
        };
        entry.FolderTreeMapping = entry.Id + ".json";

        var draft = new ArchitectureDraftViewModel(entry, isPersisted: false);
        draft.LoadMappings(new FolderTreeMapping());
        draft.MarkDirty();
        Attach(draft);
        Selected = draft;
    }

    /// <summary>Crée une architecture personnalisée à partir de la sélection, identifiant unique dérivé du libellé.</summary>
    private void Duplicate()
    {
        var source = _selected;
        if (source is null) return;
        EnsureMappingsLoaded(source);

        // Le suffixe « (Default) » des architectures par défaut ne se propage pas à une copie personnalisée
        string baseLabel = source.Label.Trim();
        if (baseLabel.EndsWith("(Default)", StringComparison.OrdinalIgnoreCase))
            baseLabel = baseLabel[..^"(Default)".Length].TrimEnd();

        var clone = source.Entry.Clone();
        clone.Id                = MakeUniqueId(ArchitectureService.MakeId(baseLabel));
        clone.Label             = string.Format(Strings.ArchEditor_CopyLabel, baseLabel);
        clone.FolderTreeMapping = clone.Id + ".json";
        clone.IsDefault         = false;
        clone.Origin            = ArchitectureOrigin.Custom;

        var draft = new ArchitectureDraftViewModel(clone, isPersisted: false);
        draft.LoadMappings(source.ToMapping());
        draft.MarkDirty();
        Attach(draft);
        Selected = draft;
    }

    private string MakeUniqueId(string baseId)
    {
        if (baseId.Length == 0) baseId = "custom";
        string id = baseId;
        for (int n = 2; Architectures.Any(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase)); n++)
            id = $"{baseId}-{n}";
        return id;
    }

    /// <summary>
    /// Efface l'architecture du dossier utilisateur, défaut compris : tout peut être supprimé, même la dernière.
    /// La fenêtre de rebase et la fenêtre principale signalent alors l'absence d'architecture, et
    /// « Restaurer les architectures par défaut » recopie les fichiers d'origine.
    /// </summary>
    private void Delete()
    {
        var draft = _selected;
        if (draft is null) return;

        string message = draft.Origin == ArchitectureOrigin.Custom
            ? string.Format(Strings.ArchEditor_DeleteCustom_Message, draft.DisplayName)
            : Strings.ArchEditor_Delete_Shipped_Message;
        if (!ShowConfirm(Strings.ArchEditor_DeleteCustom_Title, message,
                Strings.ArchEditor_Delete, Strings.General_Cancel).Result)
            return;
        if (draft.IsPersisted && !WriteToDisk(draft, () => _service.DeleteUserArchitecture(draft.Id))) return;

        int index = Architectures.IndexOf(draft);
        Detach(draft);
        Selected = Architectures.Count > 0
            ? Architectures[Math.Min(index, Architectures.Count - 1)]
            : null;
    }

    /// <summary>Recopie toutes les architectures par défaut puis recharge la liste ; les brouillons non enregistrés sont perdus.</summary>
    private void RestoreShipped()
    {
        if (!CanRestoreShipped) return;

        string message = Strings.ArchEditor_RestoreShipped_Message;
        if (HasChanges)
            message += Environment.NewLine + Environment.NewLine + Strings.ArchEditor_Discard_Message;
        if (!ShowConfirm(Strings.ArchEditor_RestoreShipped, message,
                Strings.ArchEditor_RestoreShipped, Strings.General_Cancel).Result)
            return;

        try
        {
            _service.RestoreDistributed();
            Saved = true;
        }
        catch (Exception ex)
        {
            ShowConfirm(Strings.ArchEditor_Error_Title,
                string.Format(Strings.ArchEditor_Error_Write, Strings.ArchEditor_RestoreShipped, ex.Message), "OK");
            return;
        }
        ReloadAll();
    }

    /// <summary>Exécute une écriture disque, marque Saved et rafraîchit l'état "livrées" ; false et message en cas d'échec.</summary>
    private bool WriteToDisk(ArchitectureDraftViewModel draft, Action write)
    {
        try
        {
            write();
            Saved = true;
            RefreshRestoreShippedState();
            return true;
        }
        catch (Exception ex)
        {
            ShowConfirm(Strings.ArchEditor_Error_Title,
                string.Format(Strings.ArchEditor_Error_Write, draft.DisplayName, ex.Message), "OK");
            return false;
        }
    }

    // ── Table des systèmes ────────────────────────────────────────────────

    private void AddMapping() => _selected?.Mappings.Add(new SystemMappingRowViewModel());

    private void RemoveMapping(SystemMappingRowViewModel row) => _selected?.Mappings.Remove(row);

    // ── Enregistrer / Annuler ─────────────────────────────────────────────

    /// <summary>Valide puis écrit chaque brouillon modifié ; ferme la fenêtre si tout a été écrit.</summary>
    private void Save()
    {
        var dirty  = Architectures.Where(a => a.IsDirty).ToList();
        var errors = dirty.SelectMany(a => a.Validate()).ToList();
        if (errors.Count > 0)
        {
            ShowConfirm(Strings.ArchEditor_Validation_Title, string.Join(Environment.NewLine, errors), "OK");
            return;
        }

        foreach (var draft in dirty)
        {
            try
            {
                draft.Normalize();
                _service.SaveUserArchitecture(draft.Entry, draft.ToMapping());
                draft.MarkSaved();
                Saved = true;
                RefreshRestoreShippedState();
            }
            catch (Exception ex)
            {
                ShowConfirm(Strings.ArchEditor_Validation_Title,
                    string.Format(Strings.ArchEditor_Error_Write, draft.DisplayName, ex.Message), "OK");
                return; // les brouillons déjà écrits sont marqués propres, les autres restent modifiés
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

    /// <summary>Ouvre le dossier des architectures de l'utilisateur dans l'Explorateur, en le créant si besoin.</summary>
    private void OpenFolder()
    {
        try
        {
            string path = ArchitectureService.UserArchitecturesDirectory;
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch
        {
            // Explorer indisponible : rien à faire de plus
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>Ouvre un ConfirmDialog modal avec OwnerWindow comme propriétaire (centré écran si null).</summary>
    private ConfirmDialog ShowConfirm(string title, string message, string primary, string? secondary = null)
    {
        var dlg = new ConfirmDialog(title, message, primary, secondary)
        {
            Owner = OwnerWindow,
        };
        dlg.ShowDialog();
        return dlg;
    }
}
