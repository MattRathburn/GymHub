using Duende.AccessTokenManagement.OpenIdConnect;
using Gateway;
using Gateway.Configuration;
using Gateway.Middleware;
using Gateway.Services;
using Microsoft.AspNetCore.Authentication;
using System.IdentityModel.Tokens.Jwt;

JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

var builder = WebApplication.CreateBuilder(args);

var config = builder.Configuration.AddGatewayConfiguration();
var discoService = new DiscoveryService();
var disco = await discoService.loadDiscoveryDocument(config.Authority);

builder.Services.AddDistributedMemoryCache();
builder.AddGateway(config, disco);

var app = builder.Build();
app.UseGateway();

if (string.IsNullOrEmpty(config.Url))
{
    app.Run();
}
else
{
    app.Run(config.Url);
}
