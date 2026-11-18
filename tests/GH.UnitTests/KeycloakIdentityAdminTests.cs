using System.Net;
using System.Text;
using GymHub.BuildingBlocks.Identity;
using Microsoft.Extensions.Options;

namespace GH.UnitTests;

public class KeycloakIdentityAdminTests
{
    private sealed class StubKeycloak : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, string? Auth, string Body)> Requests { get; } = [];

        public HttpStatusCode GrantStatus { get; set; } = HttpStatusCode.NoContent;

        public HttpStatusCode DeleteStatus { get; set; } = HttpStatusCode.NoContent;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add((request.Method, path, request.Headers.Authorization?.ToString(), body));

            return path switch
            {
                "/realms/GH-Test-Realm/protocol/openid-connect/token" => Json("""{"access_token":"tok","expires_in":60}"""),
                "/admin/realms/GH-Test-Realm/roles/trainer" => Json("""{"id":"role-id","name":"trainer","composite":false}"""),
                "/admin/realms/GH-Test-Realm/users/user-1/role-mappings/realm/composite" =>
                    Json("""[{"id":"r1","name":"trainer"},{"id":"r2","name":"admin"}]"""),
                "/admin/realms/GH-Test-Realm/users/user-1/role-mappings/realm" when request.Method == HttpMethod.Get =>
                    Json("""[{"id":"r1","name":"trainer"}]"""),
                "/admin/realms/GH-Test-Realm/users/user-1/role-mappings/realm" => new HttpResponseMessage(GrantStatus),
                "/admin/realms/GH-Test-Realm/users/user-1" when request.Method == HttpMethod.Delete => new HttpResponseMessage(DeleteStatus),
                "/admin/realms/GH-Test-Realm/users/user-1" when request.Method == HttpMethod.Get =>
                    Json("""{"id":"user-1","username":"alex","firstName":"Alex","enabled":true,"attributes":{"x":["y"]}}"""),
                "/admin/realms/GH-Test-Realm/users/user-1" when request.Method == HttpMethod.Put => new HttpResponseMessage(HttpStatusCode.NoContent),
                "/admin/realms/GH-Test-Realm/users/user-1/logout" => new HttpResponseMessage(HttpStatusCode.NoContent),
                "/admin/realms/GH-Test-Realm/users" =>
                    Json("""[{"id":"user-1","username":"alex","email":"alex@example.test","enabled":false,"emailVerified":true,"createdTimestamp":1767225600000}]"""),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        }

        private static HttpResponseMessage Json(string json) =>
            new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private static KeycloakIdentityAdmin Create(StubKeycloak stub, string secret = "s3cret") =>
        new(new HttpClient(stub) { BaseAddress = new Uri("http://keycloak") },
            Options.Create(new KeycloakAdminOptions { ClientSecret = secret }));

    [Fact]
    public async Task Grants_the_realm_role_with_a_client_credentials_token()
    {
        var stub = new StubKeycloak();

        await Create(stub).GrantRealmRoleAsync("user-1", "trainer", TestContext.Current.CancellationToken);

        Assert.Equal(3, stub.Requests.Count);
        Assert.Contains("grant_type=client_credentials", stub.Requests[0].Body);
        Assert.Contains("client_id=gymhub-api-admin", stub.Requests[0].Body);
        Assert.Contains("client_secret=s3cret", stub.Requests[0].Body);

        var grant = stub.Requests[2];
        Assert.Equal(HttpMethod.Post, grant.Method);
        Assert.Equal("Bearer tok", grant.Auth);
        Assert.Contains("\"id\":\"role-id\"", grant.Body);
        Assert.Contains("\"name\":\"trainer\"", grant.Body);
    }

    [Fact]
    public async Task Throws_when_Keycloak_refuses_so_the_outbox_retries()
    {
        var stub = new StubKeycloak { GrantStatus = HttpStatusCode.Forbidden };

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            Create(stub).GrantRealmRoleAsync("user-1", "trainer", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Missing_secret_is_a_configuration_error()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Create(new StubKeycloak(), secret: "").GrantRealmRoleAsync("user-1", "trainer", TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Deleting_a_user_succeeds_when_they_are_gone_already(HttpStatusCode status)
    {
        var stub = new StubKeycloak { DeleteStatus = status };

        await Create(stub).DeleteUserAsync("user-1", TestContext.Current.CancellationToken);

        Assert.Equal((HttpMethod.Delete, "Bearer tok"), (stub.Requests[1].Method, stub.Requests[1].Auth));
    }

    [Fact]
    public async Task Failed_deletes_throw_so_the_job_retries()
    {
        var stub = new StubKeycloak { DeleteStatus = HttpStatusCode.Forbidden };

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            Create(stub).DeleteUserAsync("user-1", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Disabling_keeps_the_rest_of_the_user_and_ends_their_sessions()
    {
        var stub = new StubKeycloak();

        Assert.True(await Create(stub).SetEnabledAsync("user-1", false, TestContext.Current.CancellationToken));

        var put = stub.Requests.Single(r => r.Method == HttpMethod.Put);
        Assert.Contains("\"enabled\":false", put.Body);
        Assert.Contains("\"firstName\":\"Alex\"", put.Body);
        Assert.Contains("\"attributes\"", put.Body);
        Assert.Equal("/admin/realms/GH-Test-Realm/users/user-1/logout", stub.Requests[^1].Path);
    }

    [Fact]
    public async Task Enabling_leaves_sessions_alone_and_a_missing_user_returns_false()
    {
        var stub = new StubKeycloak();

        Assert.True(await Create(stub).SetEnabledAsync("user-1", true, TestContext.Current.CancellationToken));
        Assert.DoesNotContain(stub.Requests, r => r.Path.EndsWith("/logout", StringComparison.Ordinal));
        Assert.False(await Create(stub).SetEnabledAsync("someone-else", false, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Removing_a_role_sends_the_role_and_ignores_missing_users(HttpStatusCode status)
    {
        var stub = new StubKeycloak { GrantStatus = status };

        await Create(stub).RemoveRealmRoleAsync("user-1", "trainer", TestContext.Current.CancellationToken);

        var remove = stub.Requests[^1];
        Assert.Equal(HttpMethod.Delete, remove.Method);
        Assert.Contains("\"name\":\"trainer\"", remove.Body);
    }

    [Fact]
    public async Task Search_maps_the_users_Keycloak_returns()
    {
        var stub = new StubKeycloak();

        var users = await Create(stub).SearchUsersAsync("a&b", 20, 10, TestContext.Current.CancellationToken);

        var user = Assert.Single(users);
        Assert.Equal(("user-1", "alex", false, true), (user.Id, user.Username, user.Enabled, user.EmailVerified));
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), user.CreatedAt);
    }

    [Fact]
    public async Task Reads_effective_or_direct_realm_roles_with_one_token_and_none_for_unknown_users()
    {
        var stub = new StubKeycloak();
        var admin = Create(stub);

        var effective = await admin.GetRealmRolesAsync(["user-1", "ghost"], effective: true, TestContext.Current.CancellationToken);
        var direct = await admin.GetRealmRolesAsync(["user-1"], effective: false, TestContext.Current.CancellationToken);

        Assert.Equal(new HashSet<string> { "trainer", "admin" }, effective["user-1"]);
        Assert.Empty(effective["ghost"]);
        Assert.Equal(new HashSet<string> { "trainer" }, direct["user-1"]);
        Assert.Equal(2, stub.Requests.Count(r => r.Path.EndsWith("/token", StringComparison.Ordinal)));
    }
}
