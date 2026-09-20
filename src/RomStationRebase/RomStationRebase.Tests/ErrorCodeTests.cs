using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using RomStationRebase.Helpers;
using RomStationRebase.Services;
using Xunit;

namespace RomStationRebase.Tests;

/// <summary>Codes d'erreur : un catalogue stable, sans doublon, et le bon code pour chaque échec de copie.</summary>
public class ErrorCodeTests
{
    private static IReadOnlyList<(string Name, string Code)> Catalog()
        => typeof(ErrorCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (f.Name, (string)f.GetRawConstantValue()!))
            .ToList();

    [Fact]
    public void Every_code_is_unique_and_well_formed()
    {
        var all = Catalog();
        Assert.True(all.Count >= 40);
        Assert.All(all, c => Assert.Matches(@"^RSR-[1-9]\d{3}$", c.Code));
        Assert.Equal(all.Count, all.Select(c => c.Code).Distinct().Count());
    }

    [Fact]
    public void A_message_gets_its_code_once()
    {
        Assert.Equal("Espace disque insuffisant. [RSR-3004]", ErrorCodes.Tag("Espace disque insuffisant.", ErrorCodes.DiskFull));
        Assert.Equal("[RSR-3004]", ErrorCodes.Tag(null, ErrorCodes.DiskFull));

        // Un message déjà codé (exception d'un outil reprise par le classifieur) n'est pas codé deux fois
        string once = ErrorCodes.Tag("chdman a échoué", ErrorCodes.ToolExitCode);
        Assert.Equal(once, ErrorCodes.Tag(once, ErrorCodes.CopyUnexpected));
    }

    [Fact]
    public void Copy_failures_map_to_their_code()
    {
        Assert.Equal(ErrorCodes.AccessDenied,       ErrorMessageClassifier.CodeOf(new UnauthorizedAccessException()));
        Assert.Equal(ErrorCodes.ArchiveInvalid,     ErrorMessageClassifier.CodeOf(new InvalidDataException()));
        Assert.Equal(ErrorCodes.SourceMissing,      ErrorMessageClassifier.CodeOf(new FileNotFoundException()));
        Assert.Equal(ErrorCodes.TargetUnreachable,  ErrorMessageClassifier.CodeOf(new DirectoryNotFoundException("chemin introuvable")));
        Assert.Equal(ErrorCodes.TargetDriveMissing, ErrorMessageClassifier.CodeOf(new DirectoryNotFoundException("Could not find 'F:\\'")));
        Assert.Equal(ErrorCodes.DiskFull,           ErrorMessageClassifier.CodeOf(new IOException("There is not enough space on the disk")));
        Assert.Equal(ErrorCodes.CopyInterrupted,    ErrorMessageClassifier.CodeOf(new IOException("The device is not ready")));
        Assert.Equal(ErrorCodes.CopyUnexpected,     ErrorMessageClassifier.CodeOf(new InvalidOperationException("?")));

        // Le message affiché dans la colonne Erreur porte le code
        Assert.EndsWith("[RSR-3003]", ErrorMessageClassifier.Classify(new UnauthorizedAccessException()));
    }

    [Fact]
    public void A_tool_failure_keeps_its_own_code()
    {
        var ex = new ExternalToolException(ErrorCodes.ToolStalled, "chdman ne répond plus");
        Assert.Equal(ErrorCodes.ToolStalled, ex.Code);
        Assert.Equal("chdman ne répond plus [RSR-4006]", ex.Message);
        Assert.Equal(ErrorCodes.ToolStalled, ErrorMessageClassifier.CodeOf(ex));
        Assert.Equal(ex.Message, ErrorMessageClassifier.Classify(ex)); // pas de second code
    }

    /// <summary>La documentation et les modèles de signalement citent ces codes : le format « RSR-nnnn » est un contrat.</summary>
    [Fact]
    public void Codes_can_be_found_in_any_text()
    {
        string log = "2026-09-20 12:00:00.000\tERROR\tAlundra : échec. IOException : disque plein [RSR-3004]";
        Assert.Equal("RSR-3004", Regex.Match(log, @"RSR-\d{4}").Value);
    }
}
