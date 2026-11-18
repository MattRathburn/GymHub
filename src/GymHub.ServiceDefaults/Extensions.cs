using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

public static partial class Extensions
{
    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
    {
        builder.AddBasicServiceDefaults();

        builder.Services.AddServiceDiscovery();
        builder.ConfigureForwardedHeaders();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Turn on resilience by default
            http.AddStandardResilienceHandler();

            // Turn on service discovery by default
            http.AddServiceDiscovery();
        });

        return builder;
    }

    /// <summary>
    /// Adds the services except for making outgoing HTTP calls.
    /// </summary>
    /// <remarks>
    /// This allows for things like Polly to be trimmed out of the app if it isn't used.
    /// </remarks>
    public static IHostApplicationBuilder AddBasicServiceDefaults(this IHostApplicationBuilder builder)
    {
        // Default health checks assume the event bus and self health checks
        builder.AddDefaultHealthChecks();

        builder.ConfigureOpenTelemetry();

        return builder;
    }

    public static IHostApplicationBuilder ConfigureOpenTelemetry(this IHostApplicationBuilder builder)
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter("Experimental.Microsoft.Extensions.AI");
            })
            .WithTracing(tracing =>
            {
                if (builder.Environment.IsDevelopment())
                {
                    // We want to view all traces in development
                    tracing.SetSampler(new AlwaysOnSampler());
                }

                tracing.AddAspNetCoreInstrumentation()
                    .AddGrpcClientInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddSource("Experimental.Microsoft.Extensions.AI");
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static IHostApplicationBuilder AddOpenTelemetryExporters(this IHostApplicationBuilder builder)
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.Configure<OpenTelemetryLoggerOptions>(logging => logging.AddOtlpExporter());
            builder.Services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddOtlpExporter());
            builder.Services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddOtlpExporter());
        }

        return builder;
    }

    public static IHostApplicationBuilder AddDefaultHealthChecks(this IHostApplicationBuilder builder)
    {
        builder.Services.AddHealthChecks()
            // Add a default liveness check to ensure app is responsive
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    /// <summary>
    /// X-Forwarded-For and -Proto are trusted from loopback (the default) and from the networks and proxies listed
    /// in ForwardedHeaders:KnownNetworks (CIDR) and ForwardedHeaders:KnownProxies, such as the hosting ingress.
    /// Anyone else could otherwise spoof the client IP (rate limits) or the scheme.
    /// </summary>
    public static IHostApplicationBuilder ConfigureForwardedHeaders(this IHostApplicationBuilder builder)
    {
        var section = builder.Configuration.GetSection("ForwardedHeaders");

        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            foreach (var network in section.GetSection("KnownNetworks").Get<string[]>() ?? [])
            {
                options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
            }

            foreach (var proxy in section.GetSection("KnownProxies").Get<string[]>() ?? [])
            {
                options.KnownProxies.Add(IPAddress.Parse(proxy));
            }
        });

        return builder;
    }

    /// <summary>
    /// First in the pipeline, so everything after sees the client's IP and scheme. Outside Development the trusted
    /// proxies must be configured: behind an untrusted proxy every client has the proxy's IP, so the per-IP rate
    /// limits would apply to the whole site at once. A deployment with no proxy in front says so with
    /// ForwardedHeaders:NoProxy = true.
    /// </summary>
    public static WebApplication UseDefaultForwardedHeaders(this WebApplication app)
    {
        var section = app.Configuration.GetSection("ForwardedHeaders");
        var configured = section.GetSection("KnownNetworks").Get<string[]>() is { Length: > 0 }
            || section.GetSection("KnownProxies").Get<string[]>() is { Length: > 0 }
            || section.GetValue<bool>("NoProxy");

        if (!app.Environment.IsDevelopment() && !configured)
        {
            throw new InvalidOperationException(
                "ForwardedHeaders:KnownNetworks or KnownProxies must list the proxies in front of this service (the ingress, and " +
                "for the API the Gateway's network), or set ForwardedHeaders:NoProxy to true. Otherwise every client shares " +
                "the proxy's IP and its rate limits.");
        }

        // Noted before UseForwardedHeaders consumes the headers: health endpoints answer only direct calls.
        app.Use((context, next) =>
        {
            if (IsForwarded(context.Request.Headers))
            {
                context.Items[ForwardedItem] = true;
            }

            return next(context);
        });

        app.UseForwardedHeaders();
        return app;
    }

    private const string ForwardedItem = "gymhub.forwarded";

    /// <summary>
    /// /health (every check: ready to take traffic) and /alive (the "live" checks: the process responds), in every
    /// environment for the hosting platform's probes. They answer with the status word only, no check details.
    /// Probes call the service directly; a request that came through a proxy (the Gateway forwarding /gh-api/health,
    /// or the public ingress) is the public internet and gets 404, since /health fans out to the database, storage
    /// and Keycloak and isn't rate limited.
    /// </summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.Use((context, next) =>
        {
            var path = context.Request.Path;

            if ((path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase) || path.StartsWithSegments("/alive", StringComparison.OrdinalIgnoreCase))
                && (context.Items.ContainsKey(ForwardedItem) || IsForwarded(context.Request.Headers)))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            }

            return next(context);
        });

        app.MapHealthChecks("/health").DisableRateLimiting();

        app.MapHealthChecks("/alive", new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live")
        }).DisableRateLimiting();

        return app;
    }

    /// <summary>A proxy touched the request: it carries forwarding headers (or UseForwardedHeaders' X-Original-For).</summary>
    private static bool IsForwarded(IHeaderDictionary headers) =>
        headers.ContainsKey("X-Forwarded-For") || headers.ContainsKey("X-Original-For") || headers.ContainsKey("Forwarded");
}
