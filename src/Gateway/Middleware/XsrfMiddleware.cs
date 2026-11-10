using Gateway.Configuration;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.Features;

namespace Gateway.Middleware;

public static class XsrfMiddleware
{
    public static void UseXsrfCookie(this WebApplication app) {
        app.UseXsrfCookieCreator();
        app.UseXsrfCookieChecks();
    }

    public static void UseXsrfCookieCreator(this WebApplication app)
    {
        app.Use(async (ctx, next) =>
        {
            var antiforgery = app.Services.GetService<IAntiforgery>();

            if (antiforgery == null)
            {
                throw new Exception("IAntiforgery service exptected!");
            }

            var tokens = antiforgery!.GetAndStoreTokens(ctx);

            if (tokens.RequestToken == null)
            {
                throw new Exception("token exptected!");
            }

            ctx.Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken,
                        new CookieOptions() { HttpOnly = false });

            await next(ctx);
        });
    }

    public static void UseXsrfCookieChecks(this WebApplication app)
    {
        var config = app.Services.GetRequiredService<GatewayConfigurationRecord>();
        var apiConfigs = config.ApiConfigs;

        app.Use(async (ctx, next) =>
        {
            var antiforgery = app.Services.GetService<IAntiforgery>();

            if (antiforgery == null)
            {
                throw new Exception("IAntiforgery service exptected!");
            }

            var currentUrl = ctx.Request.Path.ToString().ToLower();
            if (apiConfigs.Any(c => currentUrl.StartsWith(c.ApiPath))
                && !await IsRequestValidAsync(ctx, antiforgery))
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsJsonAsync(new
                {
                    Error = "XSRF token validadation failed"
                });
                return;
            }

            await next(ctx);
        });
    }

    /// <summary>
    /// Browsers can't add headers to a WebSocket handshake, and over HTTP/2 it is a CONNECT request, which antiforgery
    /// rejects. A WebSocket is instead accepted only from this origin, which blocks cross-site WebSocket hijacking.
    /// </summary>
    private static async Task<bool> IsRequestValidAsync(HttpContext ctx, IAntiforgery antiforgery)
    {
        if (!IsWebSocketHandshake(ctx))
        {
            return await antiforgery.IsRequestValidAsync(ctx);
        }

        var origin = ctx.Request.Headers.Origin.ToString();
        var self = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
        return string.Equals(origin, self, StringComparison.OrdinalIgnoreCase);
    }

    // YARP proxies WebSockets without the WebSockets middleware, so HttpContext.WebSockets can't tell; check the
    // HTTP/1.1 Upgrade header and the HTTP/2 extended CONNECT protocol directly.
    private static bool IsWebSocketHandshake(HttpContext ctx)
    {
        var extendedConnect = ctx.Features.Get<IHttpExtendedConnectFeature>();

        if (extendedConnect?.IsExtendedConnect == true)
        {
            return string.Equals(extendedConnect.Protocol, "websocket", StringComparison.OrdinalIgnoreCase);
        }

        return HttpMethods.IsGet(ctx.Request.Method)
            && string.Equals(ctx.Request.Headers.Upgrade.ToString(), "websocket", StringComparison.OrdinalIgnoreCase);
    }

}
