using Duende.AccessTokenManagement.OpenIdConnect;
using Gateway;
using Gateway.Configuration;
using Gateway.Middleware;
using Gateway.Services;
using Microsoft.AspNetCore.Authentication;
using System.IdentityModel.Tokens.Jwt;

JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var config = builder.Configuration.AddGatewayConfiguration();

// Outside Development the OIDC handler only accepts https metadata; say so at startup rather than on every request.
if (!builder.Environment.IsDevelopment() && !config.Authority.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException($"OpenIdConnect:Authority must use https outside Development (it is '{config.Authority}').");
}

var discoService = new DiscoveryService();
DiscoveryDocument disco;

using (var startupLogging = LoggerFactory.Create(logging => logging.AddSimpleConsole()))
{
    disco = await discoService.loadDiscoveryDocument(config.Authority, startupLogging.CreateLogger("Gateway.Startup"));
}

builder.Services.AddDistributedMemoryCache();
builder.AddGateway(config, disco);

var app = builder.Build();
app.UseHostRules();
app.UseDefaultForwardedHeaders();
app.UseSecurityHeaders();

// A published Gateway carries the built Angular app; in development YARP's "spa" route proxies to ng serve.
if (app.Environment.HasBuiltSpa())
{
    app.UseBuiltSpa();
}

app.MapDefaultEndpoints();
app.UseGateway();

if (string.IsNullOrEmpty(config.Url))
{
    app.Run();
}
else
{
    app.Run(config.Url);
}

/// <summary>Entry point, public for WebApplicationFactory in GH.GatewayTests.</summary>
public partial class Program;
