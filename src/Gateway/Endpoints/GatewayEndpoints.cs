using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace Gateway.Middleware;

public static class GatewayEndpoints
{
    public static void UseGatewayEndpoints(this WebApplication app)
    {
        app.UseUserInfoEndpoint();
        app.UseLoginEndpoint();
        app.UseLogoutEndpoint();
        app.UseGatewayStatusEndpoint();
    }

    private static void UseLogoutEndpoint(this WebApplication app)
    {
        app.MapGet("/logout", (string? redirectUrl, HttpContext ctx) =>
        {
            ctx.Session.Clear();

            var authProps = new AuthenticationProperties
            {
                RedirectUri = LocalOrRoot(redirectUrl)
            };

            var authSchemes = new string[] {
                CookieAuthenticationDefaults.AuthenticationScheme,
                OpenIdConnectDefaults.AuthenticationScheme
            };

            return Results.SignOut(authProps, authSchemes);
        });
    }

    private static void UseLoginEndpoint(this WebApplication app)
    {
        app.MapGet("/login", (string? redirectUrl) =>
            // Returned (not fire-and-forget) so the redirect to Keycloak is written before the response completes.
            Results.Challenge(new AuthenticationProperties
            {
                RedirectUri = LocalOrRoot(redirectUrl)
            }, [OpenIdConnectDefaults.AuthenticationScheme]));

        // Same as /login, but Keycloak opens its registration form.
        app.MapGet("/signup", (string? redirectUrl) =>
        {
            var properties = new AuthenticationProperties
            {
                RedirectUri = LocalOrRoot(redirectUrl)
            };
            properties.Items[PromptItem] = "create";

            return Results.Challenge(properties, [OpenIdConnectDefaults.AuthenticationScheme]);
        });
    }

    /// <summary>Authentication property carrying the OIDC "prompt" for the Keycloak redirect.</summary>
    public const string PromptItem = "gymhub.prompt";

    /// <summary>
    /// Where to return after sign-in, sign-up or sign-out: a path on this site only, so a crafted link can't send the
    /// user to another site after a real Keycloak page. Same rule as ASP.NET's IsLocalUrl.
    /// </summary>
    public static string LocalOrRoot(string? url) => IsLocalUrl(url) ? url! : "/";

    private static bool IsLocalUrl(string? url) =>
        !string.IsNullOrEmpty(url)
        && url[0] == '/'
        && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'))
        && !url.Any(char.IsControl);

    private static void UseUserInfoEndpoint(this WebApplication app)
    {
        app.MapGet("/userinfo", (ClaimsPrincipal user) =>
        {
            var claims = user.Claims;
            var dict = new Dictionary<string, string>();

            foreach (var entry in claims)
            {
                dict[entry.Type] = entry.Value;
            }

            return dict;
        });

        app.MapGet("/api/user", (ClaimsPrincipal user) =>
        {
            var isAuthenticated = user.Identity?.IsAuthenticated ?? false;
            var name = user.Identity?.Name ?? string.Empty;
            var subject = user.FindFirst("sub")?.Value ?? user.FindFirst("preferred_username")?.Value ?? string.Empty;
            // Keycloak realm roles, flattened into "roles" by the gymhub-bff client mapper.
            var roles = user.FindAll("roles").Select(r => r.Value).ToArray();

            var claims = user.Claims.Select(c => new { type = c.Type, value = c.Value }).ToArray();

            return Results.Ok(new
            {
                isAuthenticated,
                name,
                subject,
                roles,
                claims
            });
        });
    }

    private static void UseGatewayStatusEndpoint(this WebApplication app)
    {
        app.MapGet("/gatewaystatus", (ClaimsPrincipal user) =>
        {
            var dict = new Dictionary<string, string>();

            dict.Add("version", "1.0.0");

            return dict;
        });
    }

}