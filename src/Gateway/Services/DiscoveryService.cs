using Gateway.Utils;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Gateway.Services;

public class DiscoveryService {

    private const string DISCO_URL = ".well-known/openid-configuration";

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(4);
    private const int Attempts = 30;

    public static string DiscoveryUrl(string authority) => UrlUtils.CombineUrls(authority, DISCO_URL);

    /// <summary>
    /// Loads the discovery document, retrying for about two minutes: Keycloak may still be starting when the
    /// Gateway does (a deploy, a restart of both).
    /// </summary>
    public async Task<DiscoveryDocument> loadDiscoveryDocument(string authority, ILogger? logger = null) {
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var url = DiscoveryUrl(authority);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await httpClient.GetFromJsonAsync<DiscoveryDocument>(url)
                    ?? throw new InvalidOperationException("Empty discovery document from " + url);
            }
            catch (Exception ex) when (attempt < Attempts && ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                logger?.LogWarning("Keycloak discovery at {Url} failed (attempt {Attempt} of {Attempts}): {Message}", url, attempt, Attempts, ex.Message);
                await Task.Delay(RetryDelay);
            }
        }
    }
}

/// <summary>Keycloak answers the realm's discovery document (the Gateway can't sign anyone in without it).</summary>
public sealed class KeycloakHealthCheck(IHttpClientFactory httpClientFactory, Gateway.Configuration.GatewayConfigurationRecord config) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await httpClientFactory.CreateClient(nameof(KeycloakHealthCheck))
                .GetAsync(DiscoveryService.DiscoveryUrl(config.Authority), cancellationToken);

            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy($"Keycloak discovery answered {(int)response.StatusCode}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return HealthCheckResult.Unhealthy("Keycloak is unreachable.", ex);
        }
    }
}
