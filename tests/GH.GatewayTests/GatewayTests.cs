using System.Net;
using System.Text.RegularExpressions;
using Gateway.Middleware;

namespace GH.GatewayTests;

/// <summary>The Gateway's own security behaviour: headers and CSP nonce, the built SPA, hosts, health, cookies, sign-in.</summary>
public sealed partial class GatewayTests(GatewayFactory factory) : IClassFixture<GatewayFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [GeneratedRegex("'nonce-([^']+)'")]
    private static partial Regex NonceInPolicy();

    private static string Csp(HttpResponseMessage response) =>
        // Development reports instead of enforcing (the dev server can't fill in the nonce); the policy is the same.
        response.Headers.GetValues("Content-Security-Policy-Report-Only").Single();

    [Fact]
    public async Task Every_response_carries_the_security_headers()
    {
        var response = await factory.Client().GetAsync("/api/user", Ct);

        var csp = Csp(response);
        Assert.DoesNotContain("unsafe-inline", csp);
        Assert.DoesNotContain("unsafe-eval", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Contains("object-src 'none'", csp);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("camera=()", response.Headers.GetValues("Permissions-Policy").Single());
    }

    [Fact]
    public void The_policy_puts_the_nonce_on_scripts_and_styles_and_allows_media_origins()
    {
        var policy = SecurityHeaders.Policy("abc", "https://media.example");

        Assert.Contains("script-src 'self' 'nonce-abc'", policy);
        Assert.Contains("style-src 'self' 'nonce-abc'", policy);
        Assert.Contains("img-src 'self' data: blob: https://media.example", policy);
        Assert.Contains("connect-src 'self' https://media.example", policy);
    }

    [Fact]
    public async Task Client_routes_get_index_html_with_this_requests_nonce_and_no_caching()
    {
        var client = factory.Client();

        var first = await client.GetAsync("/programs/some-id", Ct);
        var second = await client.GetAsync("/programs/some-id", Ct);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("text/html", first.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-store", first.Headers.CacheControl!.ToString());

        var html = await first.Content.ReadAsStringAsync(Ct);
        var nonce = NonceInPolicy().Match(Csp(first)).Groups[1].Value;
        Assert.DoesNotContain(SpaHosting.NoncePlaceholder, html);
        Assert.Contains($"ngCspNonce=\"{nonce}\"", html);

        // A fresh nonce per request.
        Assert.NotEqual(nonce, NonceInPolicy().Match(Csp(second)).Groups[1].Value);
    }

    [Fact]
    public async Task Assets_are_cached_for_a_year_index_html_redirects_and_missing_files_are_404()
    {
        var client = factory.Client();

        var asset = await client.GetAsync($"/{GatewayFactory.Asset}", Ct);
        Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
        Assert.Contains("immutable", asset.Headers.CacheControl!.ToString());
        Assert.Equal(TimeSpan.FromDays(365), asset.Headers.CacheControl.MaxAge);

        var index = await client.GetAsync("/index.html", Ct);
        Assert.Equal(HttpStatusCode.Redirect, index.StatusCode);
        Assert.Equal("/", index.Headers.Location?.OriginalString);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/missing-file.js", Ct)).StatusCode);
    }

    [Fact]
    public async Task Other_hosts_are_refused_except_for_health_probes()
    {
        var client = factory.Client();

        using var evil = new HttpRequestMessage(HttpMethod.Get, "/programs");
        evil.Headers.Host = "evil.example";
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(evil, Ct)).StatusCode);

        // Platform probes use the container's IP as the host.
        using var probe = new HttpRequestMessage(HttpMethod.Get, "/alive");
        probe.Headers.Host = "10.1.2.3";
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(probe, Ct)).StatusCode);
    }

    [Fact]
    public void Refuses_to_start_when_ASP_NET_would_trust_forwarded_headers_from_anyone()
    {
        // ASPNETCORE_FORWARDEDHEADERS_ENABLED, which Aspire sets on published projects.
        using var trustsEveryone = factory.WithWebHostBuilder(builder => builder.UseSetting("FORWARDEDHEADERS_ENABLED", "true"));

        var refused = Assert.Throws<InvalidOperationException>(() => trustsEveryone.CreateClient());
        Assert.Contains("ASPNETCORE_FORWARDEDHEADERS_ENABLED", refused.Message);
    }

    [Fact]
    public async Task The_auth_host_passes_on_only_the_realm_and_adds_no_policy_of_its_own()
    {
        var client = factory.Client();

        using var realm = new HttpRequestMessage(HttpMethod.Get, "/realms/test/.well-known/openid-configuration");
        realm.Headers.Host = GatewayFactory.AuthHost;
        var proxied = await client.SendAsync(realm, Ct);
        Assert.Equal(HttpStatusCode.OK, proxied.StatusCode);
        Assert.Contains("authorization_endpoint", await proxied.Content.ReadAsStringAsync(Ct));
        // Keycloak sends its own policy; the Gateway's would block its inline scripts.
        Assert.False(proxied.Headers.Contains("Content-Security-Policy-Report-Only"));

        // The admin console, the master realm and the SPA are not on the auth host.
        foreach (var path in new[] { "/admin/master/console/", "/realms/master/protocol/openid-connect/token", "/programs" })
        {
            using var other = new HttpRequestMessage(HttpMethod.Get, path);
            other.Headers.Host = GatewayFactory.AuthHost;
            Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(other, Ct)).StatusCode);
        }
    }

    [Fact]
    public async Task Health_is_not_served_to_requests_that_came_through_a_proxy()
    {
        using var forwarded = new HttpRequestMessage(HttpMethod.Get, "/alive");
        forwarded.Headers.TryAddWithoutValidation("X-Forwarded-For", "203.0.113.7");

        Assert.Equal(HttpStatusCode.NotFound, (await factory.Client().SendAsync(forwarded, Ct)).StatusCode);
    }

    [Fact]
    public async Task The_XSRF_cookie_is_readable_by_the_SPA_but_secure_and_strict_and_API_calls_need_it()
    {
        var client = factory.Client();
        var response = await client.GetAsync("/api/user", Ct);

        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal)).ToLowerInvariant();
        Assert.Contains("secure", cookie);
        Assert.Contains("samesite=strict", cookie);
        Assert.DoesNotContain("httponly", cookie);

        var post = await client.PostAsync("/gh-api/api/v1/programs/", new StringContent("{}"), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, post.StatusCode);
        Assert.Contains("XSRF", await post.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Sign_in_goes_to_the_identity_provider_and_is_rate_limited()
    {
        var client = factory.Client();

        for (var i = 0; i < GatewayFactory.AuthPermitsPerMinute; i++)
        {
            var login = await client.GetAsync("/login?redirectUrl=https://evil.example/", Ct);
            Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
            Assert.StartsWith($"{factory.Authority}/protocol/openid-connect/auth", login.Headers.Location!.ToString());
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/login", Ct)).StatusCode);
    }

    [Theory]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("/programs?sort=TopRated", "/programs?sort=TopRated")]
    [InlineData("https://evil.example/", "/")]
    [InlineData("//evil.example/", "/")]
    [InlineData("/\\evil.example/", "/")]
    [InlineData("programs", "/")]
    [InlineData("/ok\r\nLocation: x", "/")]
    public void Return_urls_must_be_local_paths(string? url, string expected) =>
        Assert.Equal(expected, GatewayEndpoints.LocalOrRoot(url));

    [Theory]
    [InlineData("localhost", true)]
    [InlineData("LOCALHOST", true)]
    [InlineData("app.gymhub.example", true)]
    [InlineData("gymhub.example", false)]
    [InlineData("evilgymhub.example", false)]
    [InlineData("", false)]
    public void Host_rules_match_names_and_subdomain_wildcards(string host, bool allowed) =>
        Assert.Equal(allowed, HostRules.IsAllowed(host, ["localhost", "*.gymhub.example"]));
}
