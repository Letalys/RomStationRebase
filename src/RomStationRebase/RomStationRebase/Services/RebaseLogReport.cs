using RomStationRebase.Models;
using RomStationRebase.Resources;

namespace RomStationRebase.Services;

/// <summary>Ce que la fenêtre de rebase sait et que les options d'exécution ne portent pas : libellés affichés, chemins, présélection.</summary>
public sealed record RebaseLogContext(
    string  AppVersion,
    string  RomStationPath,
    string  DatabasePath,
    string? PresetPath,
    string  ArchiveModeText,
    string  ExtractLayoutText,
    bool    CopyCovers,
    string? MetadataFileName,
    string  MetadataLanguage,
    bool    ConvertFiles);

/// <summary>
/// En-tête du journal d'un rebase : toute la configuration utilisée, puis le plan fichier par fichier.
/// Les libellés sont ceux de la fenêtre de rebase, pour qu'une ligne du journal se rapporte à un réglage visible.
/// </summary>
public static class RebaseLogReport
{
    public static void WriteHeader(IRebaseLog log, RebaseLogContext context, RebaseOptions options)
    {
        var arch = options.Architecture;
        string yes = Strings.General_Yes, no = Strings.Log_Value_No, none = Strings.Log_Value_None;

        log.Info($"RomStation Rebase {context.AppVersion}");
        log.Info("── " + Strings.Log_Section_Config + " ──");
        log.Pair("RomStation", context.RomStationPath);
        log.Pair(Strings.Log_Label_Database, context.DatabasePath);
        log.Pair(Strings.Log_Label_Preset, context.PresetPath ?? none);
        log.Pair(Strings.Rebase_TargetPath, options.TargetPath);
        log.Pair(Strings.Rebase_Architecture, $"{arch.Label} ({arch.Id})");
        log.Pair(Strings.ArchEditor_HideGameFiles, arch.HideGameFiles ? yes : no);
        log.Pair(Strings.Rebase_Archives, context.ArchiveModeText);
        log.Pair(Strings.Rebase_ExtractLayout, context.ExtractLayoutText);
        log.Pair(Strings.Rebase_CopyCovers, context.CopyCovers
            ? $"{yes}, {arch.CoverFolder}, {arch.CoverMaxWidth} × {arch.CoverMaxHeight}"
            : no);
        log.Pair(Strings.Rebase_Group_Metadata, options.GenerateGamelist && context.MetadataFileName is not null
            ? $"{context.MetadataFileName}, {context.MetadataLanguage}"
            : none);
        log.Pair(Strings.Rebase_BackupGamelist, options.BackupGamelist ? yes : no);
        log.Pair(Strings.Rebase_Convert, context.ConvertFiles ? yes : no);
        foreach (var tool in options.Tools.OrderBy(t => t.Key, StringComparer.OrdinalIgnoreCase))
            log.Pair(string.Format(Strings.Log_Label_Tool, tool.Key), tool.Value.ExecutablePath);
        if (options.Plan.UnavailableTools.Count > 0)
            log.Warn(string.Format(Strings.Rebase_MissingTools, string.Join(", ", options.Plan.UnavailableTools)));
        log.Pair(Strings.Log_Label_WorkDirectory, options.WorkDirectory);
        log.Pair(Strings.Rebase_DuplicatePolicy, options.DuplicatePolicy == DuplicatePolicy.Overwrite
            ? Strings.Rebase_DuplicatePolicy_Overwrite
            : Strings.Rebase_DuplicatePolicy_Ignore);
        log.Pair(Strings.Rebase_ParallelCopies, options.MaxParallelCopies);
        log.Pair(Strings.Rebase_RetryAttempts, options.RetryCount);
        log.Pair(Strings.Rebase_RetryDelay, $"{options.RetryDelaySeconds} {Strings.Rebase_RetryDelayUnit}");

        var games = options.Plan.Games;
        log.Info("── " + Strings.Log_Section_Plan + " ──");
        log.Info(string.Format(Strings.Log_Plan_Summary, games.Count, games.Sum(g => g.Files.Count),
            RebaseLogExtensions.Size(options.Plan.TotalBytes)));

        foreach (var game in games)
        {
            if (game.IsUnmapped)
            {
                log.Warn($"{game.Title} ({game.SystemName}) → {Strings.Rebase_Output_Unmapped}");
                continue;
            }

            log.Info($"{game.Title} ({game.SystemName}) → {game.TargetFolder}");
            foreach (var file in game.Files)
                log.Info("  " + string.Format(Strings.Log_File_Start, KindLabel(file), file.SourcePath,
                    file.LaunchRelativePath, RebaseLogExtensions.Size(file.PlannedBytes)));
            foreach (var playlist in game.Playlists)
                log.Info("  " + string.Format(Strings.Log_KeyValue, "M3U", $"{playlist.RelativePath} ({playlist.Entries.Count})"));
            foreach (var cover in game.Covers)
                log.Info("  " + string.Format(Strings.Log_KeyValue, Strings.Log_Label_Cover, cover.DestRelativePath));
        }

        log.Info("── " + Strings.Log_Section_Run + " ──");
    }

    private static string KindLabel(RebaseFilePlan file) => file.Kind switch
    {
        FileTransferKind.Extract   => Strings.Rebase_Output_Extract,
        FileTransferKind.Transform => $"{Strings.Rebase_Column_Convert} {file.TransformToolId}",
        FileTransferKind.CopyTree  => Strings.Rebase_Output_Folder,
        _                          => Strings.Rebase_Output_Copy,
    };
}
