using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;

namespace RomStationRebase.Services;

/// <summary>
/// Ouverture d'une présélection depuis l'Explorateur : validation du chemin reçu en argument,
/// et relais de ce chemin vers l'instance déjà lancée (RSR n'a qu'une instance à la fois).
/// </summary>
public static class ShellOpenService
{
    /// <summary>Chemin absolu d'une présélection existante, ou null si l'argument n'en désigne pas un. Ne lève jamais.</summary>
    public static string? ValidatePresetPath(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > 4096) return null;
        try
        {
            // Lancé par l'Explorateur, le dossier courant est souvent System32 : le chemin doit devenir absolu tout de suite
            string full = Path.GetFullPath(raw.Trim().Trim('"'));
            return RebasePresetService.HasPresetExtension(full) && File.Exists(full) ? full : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Premier argument de la ligne de commande qui désigne une présélection, les options (-x, /x) étant ignorées.</summary>
    public static string? PresetPathFromArgs(IEnumerable<string> args)
        => args.Select(ValidatePresetPath).FirstOrDefault(p => p is not null);
}

/// <summary>
/// Canal entre instances, par tube nommé propre à l'utilisateur. La seconde instance y dépose le chemin
/// de la présélection (ou un message vide : « montre-toi »), la première l'écoute dès le splash. Aucun handle de fenêtre
/// n'est nécessaire, ce qui tient même quand la fenêtre principale est désactivée par une fenêtre modale.
/// </summary>
public sealed class SingleInstanceChannel : IDisposable
{
    // Les tubes nommés ne sont pas cloisonnés par session Windows : le SID évite toute collision entre utilisateurs
    private static readonly string DefaultPipeName = "RomStationRebase-" + (WindowsIdentity.GetCurrent().User?.Value ?? "user");

    private readonly CancellationTokenSource _cts = new();
    private readonly string _pipeName;

    /// <param name="pipeName">Nom du tube, pour les tests. Null : celui de l'application pour l'utilisateur courant.</param>
    public SingleInstanceChannel(string? pipeName = null) => _pipeName = pipeName ?? DefaultPipeName;

    /// <summary>Côté seconde instance. False si la première ne répond pas dans le délai : l'appelant se rabat sur la simple mise au premier plan.</summary>
    public static bool TrySend(string? projectPath, int timeoutMs = 1500, string? pipeName = null)
    {
        try
        {
            Helpers.NativeMethods.AllowSetForegroundWindow(Helpers.NativeMethods.ASFW_ANY); // cède le droit de passer au premier plan
            using var client = new NamedPipeClientStream(".", pipeName ?? DefaultPipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(timeoutMs);
            byte[] payload = Encoding.UTF8.GetBytes(projectPath ?? string.Empty);
            client.Write(payload, 0, payload.Length);
            client.Flush();
            return true;
        }
        catch
        {
            return false; // délai dépassé, tube absent, élévation différente entre les deux instances
        }
    }

    /// <summary>Côté première instance : écoute en boucle. <paramref name="onRequest"/> est appelé hors du thread d'interface.</summary>
    public void Start(Action<string?> onRequest) => _ = Task.Run(async () =>
    {
        var ct = _cts.Token;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);

                using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                readCts.CancelAfter(2000); // un client muet ne bloque pas le canal

                var buffer = new byte[8192];
                int total = 0, read;
                while (total < buffer.Length
                       && (read = await server.ReadAsync(buffer.AsMemory(total), readCts.Token).ConfigureAwait(false)) > 0)
                    total += read;

                onRequest(total == 0 ? null : Encoding.UTF8.GetString(buffer, 0, total));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                try { await Task.Delay(250, ct).ConfigureAwait(false); } catch { break; }
            }
        }
    });

    public void Dispose() => _cts.Cancel();
}
