using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GymHub.BuildingBlocks.Identity;

/// <summary>A user as the identity provider has them.</summary>
public sealed record IdentityUser(
    string Id, string Username, string? Email, string? FirstName, string? LastName, bool Enabled, bool EmailVerified, DateTime? CreatedAt);

/// <summary>Reads and changes users in the identity provider.</summary>
public interface IIdentityAdmin
{
    /// <summary>Users whose username, email or name contains the text (all users when it's empty), in pages.</summary>
    Task<IReadOnlyList<IdentityUser>> SearchUsersAsync(string? search, int first, int max, CancellationToken cancellationToken);

    Task<int> CountUsersAsync(string? search, CancellationToken cancellationToken);

    /// <summary>
    /// The ids of every user the realm role is mapped to directly. Users who get it from a group or a composite role
    /// aren't included; use <see cref="GetRealmRolesAsync"/> to check a user's roles.
    /// </summary>
    Task<IReadOnlySet<string>> GetRoleMemberIdsAsync(string role, CancellationToken cancellationToken);

    /// <summary>
    /// Each user's realm roles: <paramref name="effective"/> includes those from groups and composite roles (what
    /// their token carries); otherwise only the roles mapped to the user directly. A user who doesn't exist gets none.
    /// </summary>
    Task<IReadOnlyDictionary<string, IReadOnlySet<string>>> GetRealmRolesAsync(
        IReadOnlyCollection<string> userIds, bool effective, CancellationToken cancellationToken);

    /// <summary>
    /// Enables or disables the user's sign-in. Disabling also ends their sessions, so the Gateway can't refresh their
    /// tokens. Returns false when the user doesn't exist.
    /// </summary>
    Task<bool> SetEnabledAsync(string userId, bool enabled, CancellationToken cancellationToken);

    /// <summary>Adds a realm role to a user. Granting a role the user already has is a no-op.</summary>
    Task GrantRealmRoleAsync(string userId, string role, CancellationToken cancellationToken);

    /// <summary>Removes a realm role from a user. Removing a role the user doesn't have, or from a deleted user, is a no-op.</summary>
    Task RemoveRealmRoleAsync(string userId, string role, CancellationToken cancellationToken);

    /// <summary>Deletes the user and their sessions. Deleting a user who is already gone is a no-op.</summary>
    Task DeleteUserAsync(string userId, CancellationToken cancellationToken);
}

/// <summary>
/// Keycloak service-account client used for the Admin REST API. The client and its realm-management roles are
/// declared in the AppHost realm import; the secret comes from the AppHost "api-admin-client-secret" parameter.
/// </summary>
public sealed class KeycloakAdminOptions
{
    public const string SectionName = "KeycloakAdmin";

    /// <summary>Resolved through Aspire service discovery.</summary>
    public Uri BaseAddress { get; set; } = new("http://keycloak");

    public string Realm { get; set; } = "GH-Test-Realm";

    public string ClientId { get; set; } = "gymhub-api-admin";

    public string ClientSecret { get; set; } = "";
}

public static class IdentityAdminExtensions
{
    /// <summary>Registers <see cref="IIdentityAdmin"/> once, however many modules ask for it.</summary>
    public static void AddIdentityAdmin(this IHostApplicationBuilder builder)
    {
        if (builder.Services.Any(d => d.ServiceType == typeof(IIdentityAdmin)))
        {
            return;
        }

        // The realm defaults to the one the API validates tokens for (Keycloak:Realm); KeycloakAdmin:Realm overrides it.
        builder.Services.Configure<KeycloakAdminOptions>(options =>
        {
            options.Realm = builder.Configuration["Keycloak:Realm"] ?? options.Realm;
            builder.Configuration.GetSection(KeycloakAdminOptions.SectionName).Bind(options);
        });
        builder.Services.AddHttpClient<IIdentityAdmin, KeycloakIdentityAdmin>((sp, http) =>
            http.BaseAddress = sp.GetRequiredService<IOptions<KeycloakAdminOptions>>().Value.BaseAddress);

        builder.Services.AddHttpClient(nameof(KeycloakHealthCheck), (sp, http) =>
            http.BaseAddress = sp.GetRequiredService<IOptions<KeycloakAdminOptions>>().Value.BaseAddress);
        builder.Services.AddHealthChecks().AddCheck<KeycloakHealthCheck>("keycloak");
    }
}

/// <summary>Keycloak serves the realm's discovery document (tokens can't be validated without it).</summary>
public sealed class KeycloakHealthCheck(IHttpClientFactory httpClientFactory, IOptions<KeycloakAdminOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await httpClientFactory.CreateClient(nameof(KeycloakHealthCheck))
                .GetAsync($"realms/{Uri.EscapeDataString(options.Value.Realm)}/.well-known/openid-configuration", cancellationToken);

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

/// <summary>
/// Keycloak Admin REST API over a client-credentials token. A token is fetched per call: calls are rare (admin
/// actions, a trainer approval, an account deletion), so caching isn't worth the expiry handling.
/// </summary>
public sealed class KeycloakIdentityAdmin(HttpClient http, IOptions<KeycloakAdminOptions> options) : IIdentityAdmin
{
    private readonly KeycloakAdminOptions _options = options.Value;

    public async Task<IReadOnlyList<IdentityUser>> SearchUsersAsync(string? search, int first, int max, CancellationToken cancellationToken)
    {
        var token = await GetTokenAsync(cancellationToken);

        using var request = Request(HttpMethod.Get, $"users?first={first}&max={max}&briefRepresentation=true{SearchParameter(search)}", token);
        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "search users", cancellationToken);

        var users = await response.Content.ReadFromJsonAsync<List<UserRepresentation>>(cancellationToken) ?? [];
        return [.. users.Select(u => u.ToIdentityUser())];
    }

    public async Task<int> CountUsersAsync(string? search, CancellationToken cancellationToken)
    {
        var token = await GetTokenAsync(cancellationToken);

        using var request = Request(HttpMethod.Get, $"users/count?briefRepresentation=true{SearchParameter(search)}", token);
        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "count users", cancellationToken);
        return await response.Content.ReadFromJsonAsync<int>(cancellationToken);
    }

    public async Task<IReadOnlySet<string>> GetRoleMemberIdsAsync(string role, CancellationToken cancellationToken)
    {
        const int pageSize = 100;
        var token = await GetTokenAsync(cancellationToken);
        var ids = new HashSet<string>();

        for (var first = 0; ; first += pageSize)
        {
            using var request = Request(HttpMethod.Get,
                $"roles/{Uri.EscapeDataString(role)}/users?first={first}&max={pageSize}&briefRepresentation=true", token);
            using var response = await http.SendAsync(request, cancellationToken);
            await EnsureSuccessAsync(response, $"list the members of role '{role}'", cancellationToken);

            var page = await response.Content.ReadFromJsonAsync<List<UserRepresentation>>(cancellationToken) ?? [];
            ids.UnionWith(page.Select(u => u.Id));

            if (page.Count < pageSize)
            {
                return ids;
            }
        }
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlySet<string>>> GetRealmRolesAsync(
        IReadOnlyCollection<string> userIds, bool effective, CancellationToken cancellationToken)
    {
        var roles = new System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlySet<string>>();

        if (userIds.Count == 0)
        {
            return roles;
        }

        // One token for the batch, a few requests at a time.
        var token = await GetTokenAsync(cancellationToken);
        var suffix = effective ? "/composite" : "";

        await Parallel.ForEachAsync(userIds.Distinct(), new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = cancellationToken },
            async (userId, ct) =>
            {
                using var request = Request(HttpMethod.Get, $"users/{Uri.EscapeDataString(userId)}/role-mappings/realm{suffix}", token);
                using var response = await http.SendAsync(request, ct);

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    roles[userId] = new HashSet<string>();
                    return;
                }

                await EnsureSuccessAsync(response, $"read the roles of user {userId}", ct);
                var mapped = await response.Content.ReadFromJsonAsync<List<RoleRepresentation>>(ct) ?? [];
                roles[userId] = mapped.Select(r => r.Name).ToHashSet();
            });

        return roles;
    }

    /// <summary>
    /// Reads the full representation and writes it back with the new flag: a partial update can clear profile
    /// attributes under Keycloak's user profile rules.
    /// </summary>
    public async Task<bool> SetEnabledAsync(string userId, bool enabled, CancellationToken cancellationToken)
    {
        var token = await GetTokenAsync(cancellationToken);
        var user = $"users/{Uri.EscapeDataString(userId)}";

        using var get = Request(HttpMethod.Get, user, token);
        using var current = await http.SendAsync(get, cancellationToken);

        if (current.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        await EnsureSuccessAsync(current, $"read user {userId}", cancellationToken);
        var representation = await current.Content.ReadFromJsonAsync<JsonObject>(cancellationToken)
            ?? throw new InvalidOperationException($"Keycloak returned no representation for user {userId}.");
        representation["enabled"] = enabled;

        using var put = Request(HttpMethod.Put, user, token, JsonContent.Create(representation));
        using var updated = await http.SendAsync(put, cancellationToken);
        await EnsureSuccessAsync(updated, $"{(enabled ? "enable" : "disable")} user {userId}", cancellationToken);

        if (!enabled)
        {
            using var logout = Request(HttpMethod.Post, $"{user}/logout", token);
            using var loggedOut = await http.SendAsync(logout, cancellationToken);
            await EnsureSuccessAsync(loggedOut, $"end the sessions of user {userId}", cancellationToken);
        }

        return true;
    }

    public async Task GrantRealmRoleAsync(string userId, string role, CancellationToken cancellationToken)
    {
        var token = await GetTokenAsync(cancellationToken);
        var representation = await GetRoleAsync(role, token, cancellationToken);

        using var grant = Request(HttpMethod.Post, $"users/{Uri.EscapeDataString(userId)}/role-mappings/realm", token,
            JsonContent.Create(new[] { representation }));
        using var response = await http.SendAsync(grant, cancellationToken);
        await EnsureSuccessAsync(response, $"grant role '{role}' to user {userId}", cancellationToken);
    }

    public async Task RemoveRealmRoleAsync(string userId, string role, CancellationToken cancellationToken)
    {
        var token = await GetTokenAsync(cancellationToken);
        var representation = await GetRoleAsync(role, token, cancellationToken);

        using var remove = Request(HttpMethod.Delete, $"users/{Uri.EscapeDataString(userId)}/role-mappings/realm", token,
            JsonContent.Create(new[] { representation }));
        using var response = await http.SendAsync(remove, cancellationToken);

        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            await EnsureSuccessAsync(response, $"remove role '{role}' from user {userId}", cancellationToken);
        }
    }

    public async Task DeleteUserAsync(string userId, CancellationToken cancellationToken)
    {
        var token = await GetTokenAsync(cancellationToken);

        using var delete = Request(HttpMethod.Delete, $"users/{Uri.EscapeDataString(userId)}", token);
        using var response = await http.SendAsync(delete, cancellationToken);

        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            await EnsureSuccessAsync(response, $"delete user {userId}", cancellationToken);
        }
    }

    private async Task<RoleRepresentation> GetRoleAsync(string role, string token, CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Get, $"roles/{Uri.EscapeDataString(role)}", token);
        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, $"read realm role '{role}'", cancellationToken);
        return await response.Content.ReadFromJsonAsync<RoleRepresentation>(cancellationToken)
            ?? throw new InvalidOperationException($"Keycloak returned no representation for role '{role}'.");
    }

    /// <summary>A request to the realm's admin API; <paramref name="path"/> is relative to admin/realms/{realm}/.</summary>
    private HttpRequestMessage Request(HttpMethod method, string path, string token, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, $"admin/realms/{Uri.EscapeDataString(_options.Realm)}/{path}") { Content = content };
        request.Headers.Authorization = new("Bearer", token);
        return request;
    }

    private static string SearchParameter(string? search) =>
        string.IsNullOrWhiteSpace(search) ? "" : $"&search={Uri.EscapeDataString(search.Trim())}";

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

    private sealed record UserRepresentation(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("username")] string? Username,
        [property: JsonPropertyName("email")] string? Email,
        [property: JsonPropertyName("firstName")] string? FirstName,
        [property: JsonPropertyName("lastName")] string? LastName,
        [property: JsonPropertyName("enabled")] bool Enabled,
        [property: JsonPropertyName("emailVerified")] bool EmailVerified,
        [property: JsonPropertyName("createdTimestamp")] long? CreatedTimestamp)
    {
        public IdentityUser ToIdentityUser() => new(
            Id, Username ?? "", Email, FirstName, LastName, Enabled, EmailVerified,
            CreatedTimestamp is { } ms ? DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime : null);
    }

    private sealed record RoleRepresentation(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name);
}
