using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using RomStationRebase.Models;
using RomStationRebase.Resources;
using RomStationRebase.Services;

namespace RomStationRebase.ViewModels;

/// <summary>
/// Brouillon d'un outil externe dans la fenêtre des outils : copie éditable du descripteur, emplacement de
/// l'exécutable choisi par l'utilisateur, résultat du dernier test. Rien n'est écrit avant Enregistrer.
/// Les listes (extensions, arguments, codes de sortie) s'éditent en texte, une valeur par ligne ou par espace.
/// </summary>
public class ExternalToolDraftViewModel : ViewModelBase
{
    private readonly ExternalTool _tool;
    private string  _savedFingerprint;
    private string  _executablePath;
    private string  _savedExecutablePath;
    private string? _detectedPath;
    private string? _testMessage;
    private bool?   _testSucceeded;
    private bool    _isTesting;

    /// <summary>Levé quand l'état modifié change — la fenêtre en déduit son propre état.</summary>
    public event Action? DirtyChanged;

    /// <summary>Levé quand le nom ou l'emplacement de l'exécutable change : la proposition d'emplacement est à recalculer.</summary>
    public event Action<ExternalToolDraftViewModel>? ExecutableChanged;

    public ExternalToolDraftViewModel(ExternalTool tool, string? executablePath, bool isPersisted)
    {
        _tool                = tool.Clone();
        _executablePath      = executablePath ?? string.Empty;
        _savedExecutablePath = _executablePath;
        IsPersisted          = isPersisted;
        _savedFingerprint    = isPersisted ? Fingerprint() : string.Empty;
    }

    /// <summary>True si le fichier de l'outil existe dans le dossier utilisateur.</summary>
    public bool IsPersisted { get; private set; }

    public string Id => _tool.Id;
    public ArchitectureOrigin Origin => _tool.Origin;

    /// <summary>Badge de provenance : seulement pour un outil personnalisé, un défaut n'a rien à signaler.</summary>
    public string OriginText => _tool.Origin == ArchitectureOrigin.Custom ? Strings.ArchEditor_Origin_Custom : string.Empty;

    public string DisplayName => string.IsNullOrWhiteSpace(_tool.Label) ? _tool.Id : _tool.Label;

    // ── Descripteur ───────────────────────────────────────────────────────

    public string Label
    {
        get => _tool.Label;
        set { if (_tool.Label != value) { _tool.Label = value ?? string.Empty; Changed(); OnPropertyChanged(nameof(DisplayName)); } }
    }

    public string Description
    {
        get => _tool.Description;
        set { if (_tool.Description != value) { _tool.Description = value ?? string.Empty; Changed(); } }
    }

    /// <summary>Nom de fichier de l'exécutable ("chdman.exe") — son emplacement est <see cref="ExecutablePath"/>.</summary>
    public string Executable
    {
        get => _tool.Executable;
        set
        {
            if (_tool.Executable == value) return;
            _tool.Executable = value ?? string.Empty;
            Changed();
            ExecutableChanged?.Invoke(this);
        }
    }

    /// <summary>Extensions d'entrée, séparées par des espaces (".gdi .cue").</summary>
    public string InputExtensionsText
    {
        get => string.Join(" ", _tool.InputExtensions);
        set { _tool.InputExtensions = SplitExtensions(value); Changed(); }
    }

    public string OutputExtension
    {
        get => _tool.OutputExtension;
        set { if (_tool.OutputExtension != value) { _tool.OutputExtension = value ?? string.Empty; Changed(); } }
    }

    /// <summary>Arguments, un par ligne : chacun est passé tel quel au processus, sans interpréteur.</summary>
    public string ArgumentsText
    {
        get => string.Join(Environment.NewLine, _tool.Arguments);
        set { _tool.Arguments = SplitLines(value); Changed(); }
    }

    /// <summary>0 = stderr, 1 = stdout, 2 = les deux.</summary>
    public int ProgressStreamIndex
    {
        get => _tool.ProgressStream?.ToLowerInvariant() switch { "stdout" => 1, "both" => 2, _ => 0 };
        set { _tool.ProgressStream = value switch { 1 => "stdout", 2 => "both", _ => "stderr" }; Changed(); }
    }

    public string ProgressRegex
    {
        get => _tool.ProgressRegex;
        set { if (_tool.ProgressRegex != value) { _tool.ProgressRegex = value ?? string.Empty; Changed(); } }
    }

    /// <summary>Codes de sortie qui valent succès, séparés par des espaces.</summary>
    public string SuccessExitCodesText
    {
        get => string.Join(" ", _tool.SuccessExitCodes);
        set
        {
            _tool.SuccessExitCodes = Regex.Matches(value ?? string.Empty, @"-?\d+")
                .Select(m => int.TryParse(m.Value, out int n) ? n : 0).Distinct().ToList();
            Changed();
        }
    }

    public string VersionArgumentsText
    {
        get => string.Join(Environment.NewLine, _tool.VersionArguments);
        set { _tool.VersionArguments = SplitLines(value); Changed(); }
    }

    public string VersionRegex
    {
        get => _tool.VersionRegex;
        set { if (_tool.VersionRegex != value) { _tool.VersionRegex = value ?? string.Empty; Changed(); } }
    }

    public string MinVersion
    {
        get => _tool.MinVersion;
        set { if (_tool.MinVersion != value) { _tool.MinVersion = value ?? string.Empty; Changed(); } }
    }

    // ── Exécutable ────────────────────────────────────────────────────────

    /// <summary>Emplacement de l'exécutable, choisi par l'utilisateur. Vide : l'outil est indisponible, ses conversions sont écartées.</summary>
    public string ExecutablePath
    {
        get => _executablePath;
        set
        {
            if (!SetProperty(ref _executablePath, value?.Trim() ?? string.Empty)) return;
            ClearTest();
            RefreshState();
            ExecutableChanged?.Invoke(this);
        }
    }

    /// <summary>True si l'emplacement indiqué désigne un fichier présent.</summary>
    public bool IsAvailable => _executablePath.Length > 0 && File.Exists(_executablePath);

    /// <summary>Etat affiché sous le champ : prêt, introuvable, ou à indiquer.</summary>
    public string AvailabilityText
        => _executablePath.Length == 0 ? Strings.Tools_State_NotSet
         : IsAvailable                 ? Strings.Tools_State_Ready
         :                               Strings.Tools_State_Missing;

    /// <summary>Exécutable trouvé par RSR sur la machine : une proposition, que seul un clic de l'utilisateur adopte.</summary>
    public string? DetectedPath
    {
        get => _detectedPath;
        internal set
        {
            if (SetProperty(ref _detectedPath, value))
            {
                OnPropertyChanged(nameof(ShowDetected));
                OnPropertyChanged(nameof(DetectedText));
            }
        }
    }

    /// <summary>La proposition n'a d'objet que si elle diffère de l'emplacement déjà retenu.</summary>
    public bool ShowDetected => !string.IsNullOrEmpty(_detectedPath)
                                && !string.Equals(_detectedPath, _executablePath, StringComparison.OrdinalIgnoreCase);

    /// <summary>Dit d'où vient la proposition : un émulateur installé par RomStation (nom du dossier de version), ou ailleurs sur la machine.</summary>
    public string DetectedText => ExternalToolService.IsRomStationEmulatorPath(_detectedPath)
        ? string.Format(Strings.Tools_Detected_RomStation, Path.GetFileName(Path.GetDirectoryName(_detectedPath)))
        : string.Format(Strings.Tools_Detected, _detectedPath);

    // ── Test ──────────────────────────────────────────────────────────────

    public string? TestMessage
    {
        get => _testMessage;
        private set => SetProperty(ref _testMessage, value);
    }

    /// <summary>Null tant qu'aucun test n'a été fait sur cet emplacement.</summary>
    public bool? TestSucceeded
    {
        get => _testSucceeded;
        private set => SetProperty(ref _testSucceeded, value);
    }

    public bool IsTesting
    {
        get => _isTesting;
        internal set => SetProperty(ref _isTesting, value);
    }

    internal void SetTestResult(ExternalToolTestResult result)
    {
        TestSucceeded = result.Success;
        TestMessage   = result.Message;
    }

    private void ClearTest()
    {
        TestSucceeded = null;
        TestMessage   = null;
    }

    // ── Etat modifié, validation, écriture ────────────────────────────────

    public bool IsDirty => !IsPersisted
                           || _savedFingerprint != Fingerprint()
                           || !string.Equals(_savedExecutablePath, _executablePath, StringComparison.OrdinalIgnoreCase);

    private void Changed([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        OnPropertyChanged(name);
        RefreshState();
    }

    private void RefreshState()
    {
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(IsAvailable));
        OnPropertyChanged(nameof(AvailabilityText));
        OnPropertyChanged(nameof(ShowDetected));
        DirtyChanged?.Invoke();
    }

    private string Fingerprint() => JsonSerializer.Serialize(_tool);

    /// <summary>Erreurs bloquantes, préfixées du nom de l'outil. Liste vide = enregistrable.</summary>
    public List<string> Validate()
    {
        var errors = new List<string>();
        void Add(string message) => errors.Add($"{DisplayName} : {message}");

        if (string.IsNullOrWhiteSpace(_tool.Label))      Add(Strings.Tools_Validation_Label);
        if (string.IsNullOrWhiteSpace(_tool.Executable)) Add(Strings.Tools_Validation_Executable);
        if (_tool.InputExtensions.Count == 0)            Add(Strings.Tools_Validation_Input);
        if (!Regex.IsMatch(_tool.OutputExtension.Trim(), @"^\.[A-Za-z0-9]+$")) Add(Strings.Tools_Validation_Output);
        if (!_tool.Arguments.Any(a => a.Contains("{input}")) || !_tool.Arguments.Any(a => a.Contains("{output}")))
            Add(Strings.Tools_Validation_Arguments);
        foreach (string pattern in new[] { _tool.ProgressRegex, _tool.VersionRegex })
        {
            if (string.IsNullOrWhiteSpace(pattern)) continue;
            try { _ = new Regex(pattern); }
            catch (ArgumentException) { Add(string.Format(Strings.Tools_Validation_Regex, pattern)); }
        }
        return errors;
    }

    /// <summary>Descripteur à écrire, valeurs texte nettoyées.</summary>
    public ExternalTool ToModel()
    {
        var model = _tool.Clone();
        model.Label           = model.Label.Trim();
        model.Executable      = Path.GetFileName(model.Executable.Trim());
        model.OutputExtension = model.OutputExtension.Trim().ToLowerInvariant();
        return model;
    }

    public void MarkSaved()
    {
        IsPersisted          = true;
        _savedFingerprint    = Fingerprint();
        _savedExecutablePath = _executablePath;
        RefreshState();
    }

    private static List<string> SplitLines(string? text)
        => (text ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// <summary>".GDI, cue" → [".gdi", ".cue"] : le point est ajouté s'il manque.</summary>
    internal static List<string> SplitExtensions(string? text)
        => (text ?? string.Empty).Split([' ', ',', ';', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => (e.StartsWith('.') ? e : "." + e).ToLowerInvariant())
            .Distinct().ToList();
}
