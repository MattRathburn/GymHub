namespace Gateway.Middleware;

/// <summary>
/// Serves the built Angular app from wwwroot (published with the Gateway). Hashed assets are cached for a year;
/// index.html is never cached, and gets this request's CSP nonce. Without a build (development), YARP's "spa" route
/// proxies to the Angular dev server instead.
/// </summary>
public static class SpaHosting
{
    /// <summary>Angular's placeholder for the nonce, in index.html (ngCspNonce and the CSP_NONCE meta tag).</summary>
    public const string NoncePlaceholder = "**PLACEHOLDER_NONCE_SERVER**";

    public static bool HasBuiltSpa(this IWebHostEnvironment environment) =>
        environment.WebRootFileProvider.GetFileInfo("index.html").Exists;

    public static void UseBuiltSpa(this WebApplication app)
    {
        var indexFile = app.Environment.WebRootFileProvider.GetFileInfo("index.html");
        var index = File.ReadAllText(indexFile.PhysicalPath!);

        // index.html only goes out through the fallback, with the nonce filled in and without caching.
        app.Use((context, next) =>
        {
            if (context.Request.Path.Equals("/index.html", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Redirect("/");
                return Task.CompletedTask;
            }

            return next(context);
        });

        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = context =>
                context.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable"
        });

        // Client routes (no file extension) that no endpoint or proxy route matched.
        app.MapFallback("{*path:nonfile}", (HttpContext context) =>
        {
            // No nonce means no policy: the auth host, where only Keycloak's routes exist (/admin and the like are 404).
            if (context.CspNonce() is not { } nonce)
            {
                return Results.NotFound();
            }

            context.Response.Headers.CacheControl = "no-cache, no-store";
            return Results.Content(index.Replace(NoncePlaceholder, nonce, StringComparison.Ordinal), "text/html; charset=utf-8");
        });
    }
}
