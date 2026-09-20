namespace RomStationRebase.Helpers;

/// <summary>
/// Codes d'erreur affichés à la suite des messages, sous la forme « message [RSR-3004] ». Un code ne change jamais
/// de sens d'une version à l'autre : il sert à retrouver la page du wiki, à trier les signalements et, demain,
/// à automatiser une partie des corrections. Un code retiré n'est pas réattribué.
/// Le premier chiffre donne la famille : 1 démarrage, 2 contrôles avant le rebase, 3 copie et extraction,
/// 4 conversion par outil externe, 5 métadonnées et jaquettes, 6 présélections, 7 architectures et outils, 9 erreurs inattendues.
/// </summary>
public static class ErrorCodes
{
    // ── 1xxx : démarrage, RomStation et sa base ───────────────────────────
    public const string RomStationNotFound       = "RSR-1001";
    public const string DatabaseNotInitialized   = "RSR-1002";
    public const string DatabaseIncomplete       = "RSR-1003";
    public const string DatabaseLocked           = "RSR-1004";
    public const string AppStateCorrupted        = "RSR-1005";
    public const string PreferencesCorrupted     = "RSR-1006";
    public const string StartupFailed            = "RSR-1007";
    public const string LibraryLoadFailed        = "RSR-1008";
    public const string SyncFailed               = "RSR-1009";

    // ── 2xxx : contrôles avant de démarrer un rebase ──────────────────────
    public const string NoDestination            = "RSR-2001";
    public const string DestinationNotAbsolute   = "RSR-2002";
    public const string DestinationDriveMissing  = "RSR-2003";
    public const string DestinationCreateFailed  = "RSR-2004";
    public const string NoArchitecture           = "RSR-2005";
    public const string NoGameSelected           = "RSR-2006";
    public const string NotEnoughSpace           = "RSR-2007";
    public const string NotEnoughWorkSpace       = "RSR-2008";
    public const string MetadataLoadFailed       = "RSR-2009";
    public const string DestinationFolderMissing = "RSR-2010";

    // ── 3xxx : copie et extraction d'un jeu ───────────────────────────────
    public const string SourceMissing            = "RSR-3001";
    public const string TargetUnreachable        = "RSR-3002";
    public const string AccessDenied             = "RSR-3003";
    public const string DiskFull                 = "RSR-3004";
    public const string ArchiveInvalid           = "RSR-3005";
    public const string ArchiveUnsafe            = "RSR-3006";
    public const string CopyInterrupted          = "RSR-3007";
    public const string TargetDriveMissing       = "RSR-3008";
    public const string CopyUnexpected           = "RSR-3099";

    // ── 4xxx : conversion par outil externe ───────────────────────────────
    public const string ToolUnavailable          = "RSR-4001";
    public const string ToolExecutableMissing    = "RSR-4002";
    public const string ToolLaunchFailed         = "RSR-4003";
    public const string ToolExitCode             = "RSR-4004";
    public const string ToolNoOutput             = "RSR-4005";
    public const string ToolStalled              = "RSR-4006";
    public const string ToolInputMissing         = "RSR-4007";
    public const string ToolTooOld               = "RSR-4008";
    public const string ToolTestTimeout          = "RSR-4009";

    // ── 5xxx : métadonnées et jaquettes ───────────────────────────────────
    public const string MetadataWriteFailed      = "RSR-5001";
    public const string CoverCopyFailed          = "RSR-5002";

    // ── 6xxx : présélections ──────────────────────────────────────────────
    public const string PresetUnreadable         = "RSR-6001";
    public const string PresetInvalidJson        = "RSR-6002";
    public const string PresetNotAPreset         = "RSR-6003";
    public const string PresetNoGameFound        = "RSR-6004";
    public const string PresetSaveFailed         = "RSR-6005";
    public const string PresetAssociationFailed  = "RSR-6006";

    // ── 7xxx : architectures cibles et outils externes (fichiers de l'utilisateur) ──
    public const string ArchitectureLoadFailed   = "RSR-7001";
    public const string ArchitectureMappingFailed = "RSR-7002";
    public const string ArchitectureWriteFailed  = "RSR-7003";
    public const string ToolWriteFailed          = "RSR-7004";

    // ── 9xxx : erreurs inattendues ────────────────────────────────────────
    public const string UnhandledUi              = "RSR-9001";
    public const string UnhandledTask            = "RSR-9002";
    public const string UnhandledFatal           = "RSR-9003";

    /// <summary>« message [RSR-3004] ». Un message déjà suivi d'un code n'en reçoit pas un second.</summary>
    public static string Tag(string? message, string code)
    {
        message = (message ?? string.Empty).TrimEnd();
        if (string.IsNullOrEmpty(code) || message.EndsWith(']') && message.Contains("[RSR-", StringComparison.Ordinal))
            return message;
        return message.Length == 0 ? $"[{code}]" : $"{message} [{code}]";
    }
}
