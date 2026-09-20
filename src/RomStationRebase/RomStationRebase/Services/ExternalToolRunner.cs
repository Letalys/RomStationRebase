using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using RomStationRebase.Models;
using RomStationRebase.Resources;

namespace RomStationRebase.Services;

/// <summary>Échec d'un outil externe : code de sortie inattendu, blocage, exécutable introuvable. Porte les dernières lignes de sa sortie.</summary>
public sealed class ExternalToolException(string code, string message, Exception? inner = null)
    : Exception(Helpers.ErrorCodes.Tag(message, code), inner)
{
    /// <summary>Code d'erreur de la famille 4xxx, voir <see cref="Helpers.ErrorCodes"/>.</summary>
    public string Code { get; } = code;
}

/// <summary>
/// Lance un outil externe et le pilote de bout en bout : progression lue dans sa sortie, annulation par arrêt de
/// tout l'arbre de processus, arrêt automatique s'il ne dit plus rien, fichier de sortie partiel supprimé.
/// L'outil est toujours lancé sans interpréteur de commandes, arguments passés un à un.
/// </summary>
public static class ExternalToolRunner
{
    private const int TailLines = 12;

    /// <summary>Remplace les marqueurs d'un argument. Un marqueur inconnu est laissé tel quel.</summary>
    internal static string Expand(string argument, string input, string output, int threads)
        => argument
            .Replace("{input}",     input,  StringComparison.OrdinalIgnoreCase)
            .Replace("{output}",    output, StringComparison.OrdinalIgnoreCase)
            .Replace("{inputDir}",  Path.GetDirectoryName(input)  ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{outputDir}", Path.GetDirectoryName(output) ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{base}",      Path.GetFileNameWithoutExtension(output), StringComparison.OrdinalIgnoreCase)
            .Replace("{threads}",   threads.ToString(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Convertit <paramref name="input"/> en <paramref name="output"/>. <paramref name="percent"/> reçoit 0 à 100.
    /// Lève <see cref="ExternalToolException"/> en cas d'échec, <see cref="OperationCanceledException"/> en cas d'annulation ;
    /// dans les deux cas le fichier de sortie partiel est supprimé.
    /// </summary>
    /// <param name="onOutput">Reçoit la ligne de commande, puis chaque ligne écrite par l'outil hors progression. Sert au journal du rebase.</param>
    public static async Task ConvertAsync(ExternalTool tool, string executablePath, string input, string output,
        IProgress<double>? percent, CancellationToken ct, Action<string>? onOutput = null)
    {
        int threads = Math.Max(1, Environment.ProcessorCount);
        var args = tool.Arguments.Select(a => Expand(a, input, output, threads)).ToList();
        onOutput?.Invoke("\"" + executablePath + "\" " + string.Join(" ", args.Select(a => a.Contains(' ') ? "\"" + a + "\"" : a)));

        try
        {
            var result = await RunAsync(executablePath, args, Path.GetDirectoryName(input),
                tool.ProgressStream, tool.ProgressRegex, percent, tool.StallTimeoutSeconds, ct, onOutput).ConfigureAwait(false);

            if (!tool.SuccessExitCodes.Contains(result.ExitCode))
                throw new ExternalToolException(Helpers.ErrorCodes.ToolExitCode, string.Format(Strings.Tools_Error_ExitCode, tool.Executable, result.ExitCode, result.Tail).Trim());

            if (!File.Exists(output) || new FileInfo(output).Length == 0)
                throw new ExternalToolException(Helpers.ErrorCodes.ToolNoOutput, string.Format(Strings.Tools_Error_NoOutput, tool.Executable, result.Tail).Trim());
        }
        catch
        {
            // chdman efface sa sortie quand il échoue de lui-même, pas quand on le tue : on nettoie dans tous les cas
            TryDelete(output);
            throw;
        }
    }

    /// <summary>Résultat brut d'une exécution.</summary>
    public sealed record RunResult(int ExitCode, string Output, string Tail);

    /// <summary>Exécute un programme et rend son code de sortie et sa sortie complète. Sert aussi au bouton Tester.</summary>
    public static async Task<RunResult> RunAsync(string executablePath, IReadOnlyList<string> arguments, string? workingDirectory,
        string progressStream, string? progressRegex, IProgress<double>? percent, int stallTimeoutSeconds, CancellationToken ct,
        Action<string>? onOutput = null)
    {
        if (!File.Exists(executablePath))
            throw new ExternalToolException(Helpers.ErrorCodes.ToolExecutableMissing, string.Format(Strings.Tools_Test_NotFound, executablePath));

        var psi = new ProcessStartInfo
        {
            FileName               = executablePath,
            UseShellExecute        = false,   // jamais d'interpréteur : les arguments ne sont pas réinterprétés
            CreateNoWindow         = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding  = Encoding.UTF8,
        };
        if (!string.IsNullOrEmpty(workingDirectory) && Directory.Exists(workingDirectory))
            psi.WorkingDirectory = workingDirectory;
        foreach (string a in arguments) psi.ArgumentList.Add(a);

        Regex? regex = null;
        if (!string.IsNullOrWhiteSpace(progressRegex))
        {
            try { regex = new Regex(progressRegex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)); }
            catch (ArgumentException) { /* regex invalide dans un descripteur : pas de progression fine, l'outil tourne quand même */ }
        }

        using var process = new Process { StartInfo = psi };
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new ExternalToolException(Helpers.ErrorCodes.ToolLaunchFailed, string.Format(Strings.Tools_Error_Launch, Path.GetFileName(executablePath), ex.Message), ex);
        }

        var all       = new StringBuilder();
        var tail      = new Queue<string>();
        long lastSign = Environment.TickCount64;
        object gate   = new();

        void OnLine(string line, bool watched)
        {
            if (line.Length == 0) return;
            Interlocked.Exchange(ref lastSign, Environment.TickCount64);
            lock (gate)
            {
                if (all.Length < 200_000) all.AppendLine(line);
                tail.Enqueue(line);
                while (tail.Count > TailLines) tail.Dequeue();
            }
            bool isProgress = false;
            if (watched && regex is not null)
            {
                try
                {
                    var m = regex.Match(line);
                    isProgress = m.Success;
                    if (m.Success && percent is not null && m.Groups.Count > 1
                        && double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
                                           System.Globalization.CultureInfo.InvariantCulture, out double p))
                        percent.Report(Math.Clamp(p, 0, 100));
                }
                catch (RegexMatchTimeoutException) { /* ligne pathologique : ignorée */ }
            }
            // Les lignes de progression se comptent par centaines : le journal ne reçoit que le reste
            if (!isProgress) onOutput?.Invoke(line);
        }

        bool watchOut = progressStream is "stdout" or "both";
        bool watchErr = progressStream is "stderr" or "both" or "" or null;
        Task pumpOut = PumpAsync(process.StandardOutput, l => OnLine(l, watchOut));
        Task pumpErr = PumpAsync(process.StandardError,  l => OnLine(l, watchErr));

        bool stalled = false;
        using var reg = ct.Register(() => Kill(process));
        try
        {
            Task exit = process.WaitForExitAsync(CancellationToken.None);
            while (!exit.IsCompleted)
            {
                await Task.WhenAny(exit, Task.Delay(1000, CancellationToken.None)).ConfigureAwait(false);
                if (exit.IsCompleted) break;
                if (stallTimeoutSeconds > 0
                    && Environment.TickCount64 - Interlocked.Read(ref lastSign) > stallTimeoutSeconds * 1000L)
                {
                    stalled = true;
                    Kill(process);
                }
            }
            await exit.ConfigureAwait(false);
            await Task.WhenAll(pumpOut, pumpErr).ConfigureAwait(false); // vider les flux avant de lire le code de sortie
        }
        finally
        {
            Kill(process); // sans effet s'il est déjà terminé
        }

        ct.ThrowIfCancellationRequested();

        string tailText;
        string outputText;
        lock (gate)
        {
            tailText   = string.Join(" | ", tail);
            outputText = all.ToString();
        }

        if (stalled)
            throw new ExternalToolException(Helpers.ErrorCodes.ToolStalled,
                string.Format(Strings.Tools_Error_Stalled, Path.GetFileName(executablePath), stallTimeoutSeconds, tailText).Trim());

        return new RunResult(process.ExitCode, outputText, tailText);
    }

    /// <summary>
    /// Lit un flux caractère par caractère et découpe sur le retour chariot comme sur le saut de ligne :
    /// chdman réécrit sa ligne de progression avec un simple retour chariot, qu'une lecture ligne à ligne ne verrait qu'à la fin.
    /// </summary>
    private static async Task PumpAsync(StreamReader reader, Action<string> onLine)
    {
        var buffer = new char[4096];
        var line   = new StringBuilder();
        try
        {
            int read;
            while ((read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
            {
                for (int i = 0; i < read; i++)
                {
                    char c = buffer[i];
                    if (c is '\r' or '\n')
                    {
                        if (line.Length > 0) { onLine(line.ToString().Trim()); line.Clear(); }
                    }
                    else if (line.Length < 4096)
                        line.Append(c);
                }
            }
            if (line.Length > 0) onLine(line.ToString().Trim());
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // Flux fermé par l'arrêt du processus : normal en cas d'annulation
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // Déjà terminé, ou arbre partiellement inaccessible : rien de plus à faire
        }
    }

    internal static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* le dossier de travail sera supprimé de toute façon */ }
    }
}
