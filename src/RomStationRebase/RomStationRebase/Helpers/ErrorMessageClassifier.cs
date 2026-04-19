using System;
using RomStationRebase.Resources;

namespace RomStationRebase.Helpers;

/// <summary>Traduit une exception en message d'erreur localisé pour l'affichage utilisateur.</summary>
public static class ErrorMessageClassifier
{
    /// <summary>Retourne un message localisé selon le type et le contenu de l'exception.</summary>
    public static string Classify(Exception ex)
    {
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
