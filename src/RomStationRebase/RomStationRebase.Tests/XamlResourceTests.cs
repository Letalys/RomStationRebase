using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace RomStationRebase.Tests;

/// <summary>
/// Une ressource StaticResource absente ne casse ni la compilation ni les tests : elle fait planter la fenêtre à l'ouverture.
/// Ce contrôle lit les fichiers XAML comme du texte et vérifie que chaque clé citée est déclarée dans le même fichier
/// ou dans les ressources de l'application.
/// </summary>
public class XamlResourceTests
{
    private static readonly Regex KeyDeclared   = new(@"x:Key=""([^""{}]+)""", RegexOptions.Compiled);
    private static readonly Regex KeyReferenced = new(@"\{StaticResource\s+([A-Za-z_][\w.]*)\s*\}", RegexOptions.Compiled);

    [Fact]
    public void Every_static_resource_used_by_a_view_is_declared()
    {
        string project = FindProjectDirectory();

        // Ressources visibles de partout : App.xaml et les dictionnaires qu'il fusionne
        var global = new HashSet<string>(StringComparer.Ordinal);
        foreach (string file in Directory.GetFiles(Path.Combine(project, "Resources", "Styles"), "*.xaml")
                                         .Append(Path.Combine(project, "App.xaml")))
            foreach (Match m in KeyDeclared.Matches(File.ReadAllText(file)))
                global.Add(m.Groups[1].Value);

        var missing = new List<string>();
        foreach (string file in Directory.GetFiles(Path.Combine(project, "Views"), "*.xaml", SearchOption.AllDirectories))
        {
            string text  = File.ReadAllText(file);
            var    local = KeyDeclared.Matches(text).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
            foreach (Match m in KeyReferenced.Matches(text))
            {
                string key = m.Groups[1].Value;
                if (!local.Contains(key) && !global.Contains(key))
                    missing.Add($"{Path.GetFileName(file)} : {key}");
            }
        }

        Assert.True(missing.Count == 0, "Ressources citées mais jamais déclarées :\n" + string.Join("\n", missing.Distinct()));
    }

    private static string FindProjectDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "RomStationRebase", "RomStationRebase.csproj");
            if (File.Exists(candidate)) return Path.GetDirectoryName(candidate)!;
        }
        throw new DirectoryNotFoundException("Projet RomStationRebase introuvable depuis " + AppContext.BaseDirectory);
    }
}
