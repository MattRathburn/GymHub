using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace GymHub.Modules.Trainers.Identity;

/// <summary>Changes to users in the identity provider.</summary>
internal interface IIdentityAdmin
{
    /// <summary>Adds a realm role to a user. Granting a role the user already has is a no-op.</summary>
    Task GrantRealmRoleAsync(string userId, string role, CancellationToken cancellationToken);
}

/// <summary>
/// Keycloak service-account client used for the Admin REST API. The client and its realm-management roles are
/// declared in the AppHost realm import; the secret comes from the AppHost "api-admin-client-secret" parameter.
/// </summary>
internal sealed class KeycloakAdminOptions
{
    public const string SectionName = "KeycloakAdmin";

    /// <summary>Resolved through Aspire service discovery.</summary>
    public Uri BaseAddress { get; set; } = new("http://keycloak");

    public string Realm { get; set; } = "GH-Test-Realm";

    public string ClientId { get; set; } = "gymhub-api-admin";

    public string ClientSecret { get; set; } = "";
}

/// <summary>
/// Keycloak Admin REST API over a client-credentials token. A token is fetched per call: grants happen once per
/// approved trainer, so caching isn't worth the expiry handling.
/// </summary>
internal sealed class KeycloakIdentityAdmin(HttpClient http, IOptions<KeycloakAdminOptions> options) : IIdentityAdmin
{
    private readonly KeycloakAdminOptions _options = options.Value;

    public async Task GrantRealmRoleAsync(string userId, string role, CancellationToken cancellationToken)
    {
        var token = await GetTokenAsync(cancellationToken);
        var realm = Uri.EscapeDataString(_options.Realm);

        using var getRole = new HttpRequestMessage(HttpMethod.Get, $"admin/realms/{realm}/roles/{Uri.EscapeDataString(role)}");
        getRole.Headers.Authorization = new("Bearer", token);
        using var roleResponse = await http.SendAsync(getRole, cancellationToken);
        await EnsureSuccessAsync(roleResponse, $"read realm role '{role}'", cancellationToken);
        var representation = await roleResponse.Content.ReadFromJsonAsync<RoleRepresentation>(cancellationToken)
            ?? throw new InvalidOperationException($"Keycloak returned no representation for role '{role}'.");

        using var grant = new HttpRequestMessage(HttpMethod.Post, $"admin/realms/{realm}/users/{Uri.EscapeDataString(userId)}/role-mappings/realm")
        {
            Content = JsonContent.Create(new[] { representation })
        };
        grant.Headers.Authorization = new("Bearer", token);
        using var grantResponse = await http.SendAsync(grant, cancellationToken);
        await EnsureSuccessAsync(grantResponse, $"grant role '{role}' to user {userId}", cancellationToken);
    }

    private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_options.ClientSecret))
        {
            throw new InvalidOperationException($"{KeycloakAdminOptions.SectionName}:ClientSecret is not configured.");
        }

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret
        });

        using var response = await http.PostAsync($"realms/{Uri.EscapeDataString(_options.Realm)}/protocol/openid-connect/token", content, cancellationToken);
        await EnsureSuccessAsync(response, "get an admin token", cancellationToken);

        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
        return token?.AccessToken ?? throw new InvalidOperationException("Keycloak returned no access token.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string action, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Keycloak couldn't {action}: {(int)response.StatusCode} {body}", null, response.StatusCode);
        }
    }

    private sealed record TokenResponse([property: JsonPropertyName("access_token")] string? AccessToken);

    private sealed record RoleRepresentation(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name);
}
