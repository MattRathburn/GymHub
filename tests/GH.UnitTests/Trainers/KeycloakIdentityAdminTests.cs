using System.Net;
using System.Text;
using GymHub.Modules.Trainers.Identity;
using Microsoft.Extensions.Options;

namespace GH.UnitTests.Trainers;

public class KeycloakIdentityAdminTests
{
    private sealed class StubKeycloak : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, string? Auth, string Body)> Requests { get; } = [];

        public HttpStatusCode GrantStatus { get; set; } = HttpStatusCode.NoContent;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add((request.Method, path, request.Headers.Authorization?.ToString(), body));

            return path switch
            {
                "/realms/GH-Test-Realm/protocol/openid-connect/token" => Json("""{"access_token":"tok","expires_in":60}"""),
                "/admin/realms/GH-Test-Realm/roles/trainer" => Json("""{"id":"role-id","name":"trainer","composite":false}"""),
                "/admin/realms/GH-Test-Realm/users/user-1/role-mappings/realm" => new HttpResponseMessage(GrantStatus),
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
}
