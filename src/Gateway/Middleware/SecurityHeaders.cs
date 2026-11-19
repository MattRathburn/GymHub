using System.Security.Cryptography;

namespace Gateway.Middleware;

/// <summary>
/// Security headers on every Gateway response. The Content-Security-Policy carries a per-request nonce, which
/// <see cref="SpaHosting"/> writes into index.html (Angular puts it on its inline script and styles), so neither
/// scripts nor styles need 'unsafe-inline'.
/// </summary>
public static class SecurityHeaders
{
    private const string NonceItem = "gymhub.csp-nonce";

    /// <summary>The CSP nonce of this request; null on the auth host, which gets no Gateway policy.</summary>
    public static string? CspNonce(this HttpContext context) => context.Items[NonceItem] as string;

    public static void UseSecurityHeaders(this WebApplication app)
    {
        // Media is served and uploaded straight from object storage with presigned URLs.
        var mediaOrigins = string.Join(' ', app.Configuration.GetSection("Gateway:Csp:MediaOrigins").Get<string[]>() ?? []);

        // Development only reports violations: the Angular dev server (behind the Gateway there) doesn't fill in the
        // nonce, and its live reload adds scripts of its own.
        var cspHeader = app.Environment.IsDevelopment() ? "Content-Security-Policy-Report-Only" : "Content-Security-Policy";

        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
        }

        // Keycloak's pages, proxied on the auth host (published deployments), send their own CSP and framing headers;
        // a second policy would block their inline scripts.
        var authHost = app.Configuration["Gateway:AuthHost"];

        app.Use(async (context, next) =>
        {
            if (!string.IsNullOrEmpty(authHost) && string.Equals(context.Request.Host.Host, authHost, StringComparison.OrdinalIgnoreCase))
            {
                await next(context);
                return;
            }

            var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
            context.Items[NonceItem] = nonce;

            var headers = context.Response.Headers;
            headers[cspHeader] = Policy(nonce, mediaOrigins);
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

            await next(context);
        });
    }

    internal static string Policy(string nonce, string mediaOrigins) => string.Join("; ",
        "default-src 'self'",
        $"script-src 'self' 'nonce-{nonce}'",
        $"style-src 'self' 'nonce-{nonce}'",
        $"img-src 'self' data: blob: {mediaOrigins}".TrimEnd(),
        $"media-src 'self' blob: {mediaOrigins}".TrimEnd(),
        $"connect-src 'self' {mediaOrigins}".TrimEnd(),
        "font-src 'self'",
        "object-src 'none'",
        "base-uri 'self'",
        "form-action 'self'",
        "frame-ancestors 'none'");
}
