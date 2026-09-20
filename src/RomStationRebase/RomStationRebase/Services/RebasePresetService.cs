using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RomStationRebase.Models;

namespace RomStationRebase.Services;

/// <summary>Pourquoi un fichier de sélection n'a pas pu être ouvert.</summary>
public enum RebasePresetError
{
    /// <summary>Fichier absent ou illisible (droits, support retiré).</summary>
    Unreadable,
    /// <summary>Ce n'est pas du JSON valide.</summary>
    InvalidJson,
    /// <summary>JSON valide, mais pas un fichier de sélection de RomStation Rebase.</summary>
    NotASelection,
}

public sealed class RebasePresetException(RebasePresetError error, string detail, Exception? inner = null)
    : Exception(detail, inner)
{
    public RebasePresetError Error { get; } = error;
}

/// <summary>
/// Lit, écrit et rapproche les fichiers de sélection (*.rsrgp, contenu JSON). Sans dépendance à l'interface :
/// le rapprochement travaille sur des <see cref="LibraryGameRef"/>, pour rester testable.
/// </summary>
public static class RebasePresetService
{
    /// <summary>
    /// Extension propre à RSR (RomStation Rebase Game Preset), pour pouvoir l'associer à l'application.
    /// Le contenu reste du JSON lisible.
    /// </summary>
    public const string Extension = ".rsrgp";

    /// <summary>Extensions des fichiers écrits pendant le développement de la 1.3.0, toujours acceptées à l'ouverture.</summary>
    public static readonly IReadOnlyList<string> LegacyExtensions = [".rsr.json", ".rsr"];

    /// <summary>Motif d'un sélecteur de fichiers : « *.rsrgp;*.rsr.json;*.rsr ».</summary>
    public static string FilterPattern => string.Join(";", new[] { Extension }.Concat(LegacyExtensions).Select(e => "*" + e));

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented               = true,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling         = JsonCommentHandling.Skip,
        AllowTrailingCommas         = true,
        // Titres accentués lisibles dans le fichier, qui se modifie aussi à la main
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Charge un fichier de sélection. Lève <see cref="RebasePresetException"/> avec la raison de l'échec.</summary>
    public static RebasePreset Load(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path, Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new RebasePresetException(RebasePresetError.Unreadable, ex.Message, ex);
        }

        RebasePreset? file;
        try
        {
            file = JsonSerializer.Deserialize<RebasePreset>(json, JsonOpts);
        }
        catch (JsonException ex)
        {
            throw new RebasePresetException(RebasePresetError.InvalidJson, ex.Message, ex);
        }

        if (file is null
            || (!string.Equals(file.Format, RebasePreset.FormatId, StringComparison.OrdinalIgnoreCase)
                && !RebasePreset.LegacyFormatIds.Contains(file.Format, StringComparer.OrdinalIgnoreCase)))
            throw new RebasePresetException(RebasePresetError.NotASelection, Path.GetFileName(path));

        file.Settings ??= new RebasePresetSettings();
        file.Games    = (file.Games ?? []).Where(g => g is not null).ToList();
        return file;
    }

    /// <summary>Écrit le fichier par un temporaire voisin : une écriture interrompue ne laisse jamais une sélection tronquée.</summary>
    public static void Save(string path, RebasePreset file)
    {
        string? parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent))
            Directory.CreateDirectory(parent);

        string temp = path + ".tmp";
        File.WriteAllText(temp, Serialize(file), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temp, path, overwrite: true);
    }

    public static string Serialize(RebasePreset file)
    {
        file.Format = RebasePreset.FormatId; // signature posée à l'écriture, jamais par défaut
        return JsonSerializer.Serialize(file, JsonOpts);
    }

    /// <summary>Pose l'extension .rsrgp si le nom saisi ne la porte pas déjà. Un ".json" ou une ancienne extension est remplacé, jamais doublé.</summary>
    public static string EnsureExtension(string path)
    {
        if (path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)) return path;
        foreach (string old in LegacyExtensions.Append(".json"))
            if (path.EndsWith(old, StringComparison.OrdinalIgnoreCase))
                return path[..^old.Length] + Extension;
        return path + Extension;
    }

    /// <summary>True si le fichier porte l'extension des présélections, actuelle ou ancienne.</summary>
    public static bool HasPresetExtension(string path)
        => path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)
        || LegacyExtensions.Any(old => path.EndsWith(old, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Rapproche les jeux du fichier de ceux de la bibliothèque : par identifiant RomStation d'abord,
    /// puis par titre et système si l'identifiant est inconnu et qu'un seul jeu correspond.
    /// Un jeu de la bibliothèque n'est attribué qu'une fois.
    /// </summary>
    public static RebasePresetMatch Match(RebasePreset file, IReadOnlyList<LibraryGameRef> library)
    {
        var result = new RebasePresetMatch();

        var byRid = new Dictionary<int, List<LibraryGameRef>>();
        foreach (var g in library)
        {
            if (g.Rid <= 0) continue;
            if (!byRid.TryGetValue(g.Rid, out var list)) byRid[g.Rid] = list = [];
            list.Add(g);
        }

        foreach (var wanted in file.Games)
        {
            LibraryGameRef? hit = null;
            bool byTitle = false;

            if (wanted.Rid > 0 && byRid.TryGetValue(wanted.Rid, out var candidates))
            {
                // Même rid sur deux systèmes : le système du fichier départage
                hit = candidates.FirstOrDefault(c => SameText(c.System, wanted.System) && !result.Matched.ContainsKey(c.Id))
                   ?? candidates.FirstOrDefault(c => !result.Matched.ContainsKey(c.Id));
            }

            if (hit is null && !string.IsNullOrWhiteSpace(wanted.Title))
            {
                var sameTitle = library
                    .Where(c => SameText(c.Title, wanted.Title) && SameText(c.System, wanted.System)
                                && !result.Matched.ContainsKey(c.Id))
                    .Take(2).ToList();
                if (sameTitle.Count == 1)
                {
                    hit = sameTitle[0];
                    byTitle = true;
                }
            }

            if (hit is null)
            {
                result.Missing.Add(wanted);
                continue;
            }

            result.Matched[hit.Id] = wanted;
            if (byTitle) result.MatchedByTitle.Add(wanted);
        }

        return result;
    }

    private static bool SameText(string a, string b)
        => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
}
