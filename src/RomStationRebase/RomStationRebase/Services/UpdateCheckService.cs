using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using RomStationRebase.Models;

namespace RomStationRebase.Services;

/// <summary>Résultat d'une vérification de mise à jour.</summary>
public enum UpdateCheckOutcome
{
    /// <summary>L'application est à jour.</summary>
    UpToDate,
    /// <summary>Une mise à jour est disponible.</summary>
    UpdateAvailable,
    /// <summary>La vérification a échoué (réseau, timeout, parsing).</summary>
    Error
}

/// <summary>Résultat structuré d'une vérification de mise à jour.</summary>
public sealed class UpdateCheckResult
{
    public UpdateCheckOutcome Outcome          { get; init; }
    public string?            AvailableVersion { get; init; }
    public string?            ReleaseUrl       { get; init; }
}

/// <summary>
/// Service de vérification des mises à jour disponibles via l'API GitHub Releases.
/// L'application ne télécharge ni n'installe rien — elle se contente de notifier.
/// </summary>
public class UpdateCheckService
{
    private static readonly HttpClient _http = CreateHttpClient();
    private readonly AppMetadata _metadata;

    public UpdateCheckService(AppMetadata metadata)
    {
        _metadata = metadata;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        // L'API GitHub retourne 403 sans User-Agent.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RomStationRebase");
        return client;
    }

    /// <summary>
    /// Effectue la requête HTTP, parse la réponse, compare la version distante à la version locale de
    /// l'assembly. Tout échec (réseau, timeout, parsing, 4xx/5xx) renvoie UpdateCheckOutcome.Error
    /// sans propager l'exception.
    /// </summary>
    public async Task<UpdateCheckResult> CheckAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_metadata.ApiReleasesUrl))
                return new UpdateCheckResult { Outcome = UpdateCheckOutcome.Error };

            using var response = await _http.GetAsync(_metadata.ApiReleasesUrl).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new UpdateCheckResult { Outcome = UpdateCheckOutcome.Error };

            string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var dto = JsonSerializer.Deserialize<GitHubReleaseDto>(json);
            if (dto == null || string.IsNullOrWhiteSpace(dto.TagName))
                return new UpdateCheckResult { Outcome = UpdateCheckOutcome.Error };

            // Garde défensive : /releases/latest exclut déjà les drafts et prereleases.
            if (dto.Draft || dto.Prerelease)
                return new UpdateCheckResult { Outcome = UpdateCheckOutcome.UpToDate };

            if (!Version.TryParse(dto.TagName.TrimStart('v'), out var remoteVersion))
                return new UpdateCheckResult { Outcome = UpdateCheckOutcome.Error };

            var localVersion = Assembly.GetEntryAssembly()?.GetName().Version;
            if (localVersion == null)
                return new UpdateCheckResult { Outcome = UpdateCheckOutcome.Error };

            if (remoteVersion > localVersion)
            {
                return new UpdateCheckResult
                {
                    Outcome          = UpdateCheckOutcome.UpdateAvailable,
                    AvailableVersion = dto.TagName,
                    ReleaseUrl       = dto.HtmlUrl,
                };
            }

            return new UpdateCheckResult { Outcome = UpdateCheckOutcome.UpToDate };
        }
        catch
        {
            return new UpdateCheckResult { Outcome = UpdateCheckOutcome.Error };
        }
    }

    private sealed class GitHubReleaseDto
    {
        [JsonPropertyName("tag_name")]   public string TagName    { get; init; } = string.Empty;
        [JsonPropertyName("html_url")]   public string HtmlUrl    { get; init; } = string.Empty;
        [JsonPropertyName("draft")]      public bool   Draft      { get; init; }
        [JsonPropertyName("prerelease")] public bool   Prerelease { get; init; }
    }
}
