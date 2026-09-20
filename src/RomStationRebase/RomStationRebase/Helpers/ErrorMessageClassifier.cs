using System;
using RomStationRebase.Resources;

namespace RomStationRebase.Helpers;

/// <summary>Traduit une exception en message d'erreur localisé pour l'affichage utilisateur.</summary>
public static class ErrorMessageClassifier
{
    /// <summary>Message localisé suivi de son code d'erreur : « Espace disque insuffisant… [RSR-3004] ».</summary>
    public static string Classify(Exception ex) => ErrorCodes.Tag(Describe(ex), CodeOf(ex));

    /// <summary>Code d'erreur de l'exception, selon les mêmes critères que le message.</summary>
    public static string CodeOf(Exception ex)
    {
        if (ex is Services.ExternalToolException tool)                                     return tool.Code;
        if (ex is Services.UnsafeArchiveException)                                         return ErrorCodes.ArchiveUnsafe;
        if (ex is System.IO.InvalidDataException)                                          return ErrorCodes.ArchiveInvalid;
        if (ex is System.IO.FileNotFoundException)                                         return ErrorCodes.SourceMissing;
        if (ex is System.IO.DirectoryNotFoundException && LooksLikeDriveLetter(ex.Message)) return ErrorCodes.TargetDriveMissing;
        if (ex is System.IO.DirectoryNotFoundException)                                    return ErrorCodes.TargetUnreachable;
        if (ex is UnauthorizedAccessException)                                             return ErrorCodes.AccessDenied;
        if (ex is System.IO.IOException io)
        {
            string msg = io.Message?.ToLowerInvariant() ?? string.Empty;
            if (msg.Contains("not enough space") || msg.Contains("disk full")
                || msg.Contains("insufficient") || msg.Contains("espace"))
                return ErrorCodes.DiskFull;
            if (msg.Contains("could not find a part of the path") || msg.Contains("introuvable"))
                return ErrorCodes.TargetUnreachable;
            return ErrorCodes.CopyInterrupted;
        }
        return ErrorCodes.CopyUnexpected;
    }

    /// <summary>Retourne un message localisé selon le type et le contenu de l'exception.</summary>
    private static string Describe(Exception ex)
    {
        // Archives : une entrée qui remonte hors du dossier cible, ou un zip illisible
        if (ex is Services.UnsafeArchiveException)
            return Strings.Rebase_Error_UnsafeArchive;
        if (ex is System.IO.InvalidDataException)
            return Strings.Rebase_Error_ArchiveInvalid;
        if (ex is System.IO.DirectoryNotFoundException && LooksLikeDriveLetter(ex.Message))
            return Strings.Rebase_Error_InvalidDriveLetter;
        if (ex is System.IO.DirectoryNotFoundException)
            return Strings.Rebase_Error_DirectoryNotFound;
        if (ex is UnauthorizedAccessException)
            return Strings.Rebase_Error_AccessDenied;
        if (ex is System.IO.IOException ioEx)
        {
            string msg = ioEx.Message?.ToLowerInvariant() ?? string.Empty;
            if (msg.Contains("not enough space") || msg.Contains("disk full")
                || msg.Contains("insufficient") || msg.Contains("espace"))
                return Strings.Rebase_Error_DiskFull;
            if (msg.Contains("could not find a part of the path")
                || msg.Contains("introuvable"))
                return Strings.Rebase_Error_DirectoryNotFound;
            return Strings.Rebase_Error_Interrupted;
        }
        return ex.Message;
    }

    /// <summary>Heuristique : le message d'exception ressemble-t-il à une lettre de lecteur invalide ?</summary>
    private static bool LooksLikeDriveLetter(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return false;
        return System.Text.RegularExpressions.Regex.IsMatch(message, @"['""\s][A-Za-z]:\\?['""\s]?");
    }
}
