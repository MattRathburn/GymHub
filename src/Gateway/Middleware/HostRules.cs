namespace Gateway.Middleware;

/// <summary>
/// Only the site's own host names (Gateway:AllowedHosts: exact names, "*.example.com" for subdomains, or "*") are
/// answered; anything else gets 400. ASP.NET's AllowedHosts filter is left open ("*") because it can't spare the
/// health probes, which the hosting platform sends to the container's IP: /health and /alive pass whatever the Host.
/// (They only answer direct calls anyway; see MapDefaultEndpoints.)
/// </summary>
public static class HostRules
{
    public static void UseHostRules(this WebApplication app)
    {
        var allowed = app.Configuration.GetSection("Gateway:AllowedHosts").Get<string[]>() ?? ["localhost"];

        if (allowed.Contains("*"))
        {
            return;
        }

        app.Use((context, next) =>
        {
            var path = context.Request.Path;
            var probe = path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase)
                || path.StartsWithSegments("/alive", StringComparison.OrdinalIgnoreCase);

            if (!probe && !IsAllowed(context.Request.Host.Host, allowed))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return Task.CompletedTask;
            }

            return next(context);
        });
    }

    internal static bool IsAllowed(string host, IReadOnlyList<string> allowed) =>
        !string.IsNullOrEmpty(host) && allowed.Any(rule => rule.StartsWith("*.", StringComparison.Ordinal)
            ? host.EndsWith(rule[1..], StringComparison.OrdinalIgnoreCase) && host.Length > rule.Length - 1
            : string.Equals(host, rule, StringComparison.OrdinalIgnoreCase));
}
