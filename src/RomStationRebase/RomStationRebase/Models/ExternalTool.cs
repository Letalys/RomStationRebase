namespace RomStationRebase.Models;

/// <summary>
/// Outil externe de conversion, décrit de façon déclarative : un exécutable en ligne de commande qui prend
/// un fichier et en rend un autre (chdman : .gdi → .chd). RSR le lance lui-même pendant le rebase, suit sa
/// progression et nettoie derrière lui. Aucun script, aucun interpréteur : les arguments sont une liste,
/// passés un à un au processus, si bien qu'un nom de jeu ne peut rien injecter.
/// </summary>
public class ExternalTool
{
    public string Id          { get; set; } = string.Empty;
    public string Label       { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>Nom de fichier de l'exécutable (ex : "chdman.exe"). Son emplacement est choisi par l'utilisateur, jamais écrit ici.</summary>
    public string Executable { get; set; } = string.Empty;

    /// <summary>Page où se procurer l'outil : RSR ne le distribue pas.</summary>
    public string DownloadUrl { get; set; } = string.Empty;

    /// <summary>Dossiers où chercher l'exécutable pour le proposer (variables d'environnement acceptées). Une proposition, jamais un choix automatique.</summary>
    public List<string> SearchPaths { get; set; } = [];

    /// <summary>Extensions acceptées en entrée, avec le point (".gdi", ".cue").</summary>
    public List<string> InputExtensions { get; set; } = [];

    /// <summary>Extension du fichier produit, avec le point (".chd").</summary>
    public string OutputExtension { get; set; } = string.Empty;

    /// <summary>Arguments, un par élément. Marqueurs : {input}, {output}, {inputDir}, {outputDir}, {base}, {threads}.</summary>
    public List<string> Arguments { get; set; } = [];

    /// <summary>Flux où l'outil écrit sa progression : "stderr" (chdman), "stdout" ou "both".</summary>
    public string ProgressStream { get; set; } = "stderr";

    /// <summary>Expression régulière dont le premier groupe capture un pourcentage. Vide : pas de progression fine.</summary>
    public string ProgressRegex { get; set; } = string.Empty;

    /// <summary>Codes de sortie qui valent succès.</summary>
    public List<int> SuccessExitCodes { get; set; } = [0];

    /// <summary>Arguments qui font afficher sa version à l'outil (bouton Tester).</summary>
    public List<string> VersionArguments { get; set; } = [];

    /// <summary>Expression régulière dont le premier groupe capture la version dans la sortie du test.</summary>
    public string VersionRegex { get; set; } = string.Empty;

    /// <summary>Version minimale exigée (ex : "0.230"). Vide : aucune exigence.</summary>
    public string MinVersion { get; set; } = string.Empty;

    /// <summary>Taille attendue du résultat rapportée à celle de l'entrée (0,55 pour un CHD). Sert à l'estimation, annoncée comme telle.</summary>
    public double SizeRatioHint { get; set; } = 1.0;

    /// <summary>Sans aucune sortie de l'outil pendant ce délai, RSR le considère bloqué et l'arrête.</summary>
    public int StallTimeoutSeconds { get; set; } = 300;

    /// <summary>Provenance, posée au chargement. Jamais sérialisée.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public ArchitectureOrigin Origin { get; set; } = ArchitectureOrigin.Distributed;

    public ExternalTool Clone() => new()
    {
        Id = Id, Label = Label, Description = Description, Executable = Executable, DownloadUrl = DownloadUrl,
        SearchPaths = [.. SearchPaths], InputExtensions = [.. InputExtensions], OutputExtension = OutputExtension,
        Arguments = [.. Arguments], ProgressStream = ProgressStream, ProgressRegex = ProgressRegex,
        SuccessExitCodes = [.. SuccessExitCodes], VersionArguments = [.. VersionArguments],
        VersionRegex = VersionRegex, MinVersion = MinVersion, SizeRatioHint = SizeRatioHint,
        StallTimeoutSeconds = StallTimeoutSeconds, Origin = Origin,
    };

    /// <summary>True si l'outil accepte cette extension en entrée.</summary>
    public bool Accepts(string extension)
        => InputExtensions.Any(e => string.Equals(e, extension, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Index du dossier utilisateur des outils : mémoire des défauts déjà copiés, comme pour les architectures.</summary>
public class ExternalToolIndex
{
    public List<string> SeededDefaults { get; set; } = [];

    /// <summary>
    /// Emplacement de l'exécutable de chaque outil, par identifiant d'outil. Choisi par l'utilisateur, outil par outil,
    /// même quand plusieurs outils partagent un programme (les trois chdman) : chaque outil a sa configuration propre.
    /// RSR ne lance jamais un programme qu'il aurait trouvé tout seul.
    /// </summary>
    public Dictionary<string, string> ExecutablePaths { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Ce que le planificateur a besoin de savoir d'un outil : sans accès disque, donc sans chemin d'exécutable.</summary>
public sealed record PlanTool(string Id, string Label, IReadOnlyList<string> InputExtensions, string OutputExtension,
                              double SizeRatioHint, bool IsAvailable)
{
    public bool Accepts(string extension)
        => InputExtensions.Any(e => string.Equals(e, extension, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Résultat du bouton Tester d'un outil.</summary>
public sealed record ExternalToolTestResult(bool Success, string? Version, string Message);
