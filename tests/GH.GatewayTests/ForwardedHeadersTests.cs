using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GH.GatewayTests;

/// <summary>
/// ServiceDefaults' forwarded headers as published to Azure: private networks are trusted proxies, and the API walks
/// back two hops (its ingress, then the Gateway) to the browser's address.
/// </summary>
public class ForwardedHeadersTests
{
    private const string Browser = "198.51.100.7";
    private const string GatewayReplica = "10.0.4.12";
    private const string Ingress = "100.100.0.5";

    private static async Task<IPAddress?> ClientAddressAsync(int? forwardLimit, string forwardedFor)
    {
        var settings = new Dictionary<string, string?> { ["ForwardedHeaders:KnownNetworks:0"] = "10.0.0.0/8", ["ForwardedHeaders:KnownNetworks:1"] = "100.64.0.0/10" };

        if (forwardLimit is not null)
        {
            settings["ForwardedHeaders:ForwardLimit"] = forwardLimit.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.ConfigureForwardedHeaders();
        using var services = builder.Services.BuildServiceProvider();

        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(Ingress);
        context.Request.Headers["X-Forwarded-For"] = forwardedFor;

        var middleware = new ForwardedHeadersMiddleware(_ => Task.CompletedTask, NullLoggerFactory.Instance,
            services.GetRequiredService<IOptions<ForwardedHeadersOptions>>());
        await middleware.Invoke(context);

        return context.Connection.RemoteIpAddress;
    }

    [Fact]
    public async Task The_API_walks_back_through_its_ingress_and_the_Gateway_to_the_browser()
    {
        // The browser's address as the Gateway passed it on, then the Gateway replica's, appended by the API's ingress.
        Assert.Equal(IPAddress.Parse(Browser), await ClientAddressAsync(2, $"{Browser}, {GatewayReplica}"));

        // With the default single hop it would stop at the Gateway, and every anonymous visitor would share its limit.
        Assert.Equal(IPAddress.Parse(GatewayReplica), await ClientAddressAsync(null, $"{Browser}, {GatewayReplica}"));
    }

    [Fact]
    public async Task A_forged_private_address_from_the_browser_is_not_believed()
    {
        // The browser claims to be 10.9.9.9; the walk stops at its real, public address.
        Assert.Equal(IPAddress.Parse(Browser), await ClientAddressAsync(2, $"10.9.9.9, {Browser}"));
    }
}
