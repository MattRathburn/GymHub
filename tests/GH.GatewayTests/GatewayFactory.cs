using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace GH.GatewayTests;

/// <summary>
/// Runs the Gateway in Development with a published-style wwwroot (an index.html with Angular's nonce placeholder and
/// one hashed asset) and a stub OpenID provider on a loopback port, which answers the discovery document the Gateway
/// loads at startup. Nothing behind the Gateway runs: requests that would reach the API or Keycloak aren't made.
/// </summary>
public sealed class GatewayFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Asset = "main-ABC123.js";
    public const int AuthPermitsPerMinute = 3;
    public const string AuthHost = "auth.localhost";

    private readonly string _webRoot = Directory.CreateTempSubdirectory("gymhub-gateway-tests").FullName;
    private WebApplication? _identityProvider;

    /// <summary>The stub provider's realm address: the Gateway's metadata authority.</summary>
    public string Authority { get; private set; } = "";

    public async ValueTask InitializeAsync()
    {
        File.WriteAllText(Path.Combine(_webRoot, "index.html"),
            """<!doctype html><html><head><meta name="CSP_NONCE" content="**PLACEHOLDER_NONCE_SERVER**"></head><body><app-root ngCspNonce="**PLACEHOLDER_NONCE_SERVER**"></app-root></body></html>""");
        File.WriteAllText(Path.Combine(_webRoot, Asset), "console.log('app');");

        var provider = WebApplication.CreateSlimBuilder();
        provider.WebHost.UseUrls("http://127.0.0.1:0");
        _identityProvider = provider.Build();
        _identityProvider.MapGet("/realms/test/.well-known/openid-configuration", (HttpContext context) =>
        {
            var issuer = $"{context.Request.Scheme}://{context.Request.Host}/realms/test";
            return Results.Json(new Dictionary<string, object>
            {
                ["issuer"] = issuer,
                ["authorization_endpoint"] = $"{issuer}/protocol/openid-connect/auth",
                ["token_endpoint"] = $"{issuer}/protocol/openid-connect/token",
                ["end_session_endpoint"] = $"{issuer}/protocol/openid-connect/logout",
                ["jwks_uri"] = $"{issuer}/protocol/openid-connect/certs",
                ["response_types_supported"] = new[] { "code" },
                ["subject_types_supported"] = new[] { "public" },
                ["id_token_signing_alg_values_supported"] = new[] { "RS256" }
            });
        });
        _identityProvider.MapGet("/realms/test/protocol/openid-connect/certs", () => Results.Json(new { keys = Array.Empty<object>() }));
        await _identityProvider.StartAsync();

        var address = _identityProvider.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        Authority = $"{address}/realms/test";
    }

    public new async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();

        if (_identityProvider is not null)
        {
            await _identityProvider.DisposeAsync();
        }

        Directory.Delete(_webRoot, recursive: true);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting(WebHostDefaults.WebRootKey, _webRoot);
        // As published: browsers use the public authority, which nothing answers here, and the Gateway reads the metadata
        // from the provider's private address. Starting at all proves it never fetches from the public one.
        builder.UseSetting("OpenIdConnect:Authority", "https://auth.invalid/realms/test");
        builder.UseSetting("OpenIdConnect:MetadataAuthority", Authority);
        builder.UseSetting("OpenIdConnect:ClientId", "gymhub-bff");
        builder.UseSetting("OpenIdConnect:ClientSecret", "test-secret");
        builder.UseSetting("RateLimiting:AuthPermitsPerMinute", AuthPermitsPerMinute.ToString(System.Globalization.CultureInfo.InvariantCulture));

        // As published: the Gateway also answers on the auth host and passes on only the realm's routes to Keycloak
        // (here the stub provider).
        builder.UseSetting("Gateway:AllowedHosts:0", "localhost");
        builder.UseSetting("Gateway:AllowedHosts:1", AuthHost);
        builder.UseSetting("Gateway:AuthHost", AuthHost);
        builder.UseSetting("ReverseProxy:Routes:keycloak-realm:ClusterId", "keycloak");
        builder.UseSetting("ReverseProxy:Routes:keycloak-realm:Match:Hosts:0", AuthHost);
        builder.UseSetting("ReverseProxy:Routes:keycloak-realm:Match:Path", "/realms/test/{**catch-all}");
        builder.UseSetting("ReverseProxy:Clusters:keycloak:Destinations:keycloak:Address", new Uri(Authority).GetLeftPart(UriPartial.Authority));
    }

    /// <summary>A client that doesn't follow redirects, over https (the cookies are Secure).</summary>
    public HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost")
    });
}
