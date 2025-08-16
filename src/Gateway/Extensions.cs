using Gateway.Transformers;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;
using System.Security.Claims;
using Yarp.ReverseProxy.Transforms;

namespace Gateway;

internal static class Extensions
{
    public static IServiceCollection AddReverseProxy(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<AddBearerTokenToHeadersTransform>();
        services.AddSingleton<AddAntiforgeryTokenResponseTransform>();
        services.AddSingleton<ValidateAntiforgeryTokenRequestTransform>();

        services
            .AddReverseProxy()
            .LoadFromConfig(configuration.GetSection("ReverseProxy"))
            .AddTransforms(builderContext =>
            {
                builderContext.ResponseTransforms.Add(builderContext.Services.GetRequiredService<AddAntiforgeryTokenResponseTransform>());
                builderContext.RequestTransforms.Add(builderContext.Services.GetRequiredService<ValidateAntiforgeryTokenRequestTransform>());

                if (!string.IsNullOrEmpty(builderContext.Route.AuthorizationPolicy))
                {
                    builderContext.RequestTransforms.Add(builderContext.Services.GetRequiredService<AddBearerTokenToHeadersTransform>());
                }
                builderContext.RequestTransforms.Add(new RequestHeaderRemoveTransform("Cookie"));
            })
            .AddServiceDiscoveryDestinationResolver();

        return services;
    }

    public static IServiceCollection AddAuthenticationSchemes(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAuthentication(options =>
        {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
        })
        .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
        {
            options.Cookie.Name = "__Gymhub";
            options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        })
        //.AddKeycloakJwtBearer(
        //    serviceName: "keycloak",
        //    realm: "test-realm",
        //    options =>
        //    {
        //        options.Audience = "plan.api";
        //    }
        //);

        .AddKeycloakOpenIdConnect(serviceName: "keycloak", realm: "test-realm", options =>
        {
            options.Authority = $"{configuration.GetValue<string>("KeycloakAuth:KC_Authority")}";
            options.ClientId = configuration.GetValue<string>("KeycloakAuth:KC_ClientId");
            options.ClientSecret = configuration.GetValue<string>("KeycloakAuth:KC_ClientSecret");

            options.ResponseType = OpenIdConnectResponseType.Code;
            options.ResponseMode = OpenIdConnectResponseMode.Query;

            options.GetClaimsFromUserInfoEndpoint = true;
            options.SaveTokens = true;
            options.MapInboundClaims = false;

            options.RequireHttpsMetadata = false;

            options.TokenValidationParameters = new TokenValidationParameters
            {
                NameClaimType = ClaimTypes.NameIdentifier,
                RoleClaimType = ClaimTypes.Role,
            };

            options.Scope.Clear();
            options.Scope.Add("openid");
            options.Scope.Add("profile");
            options.Scope.Add("offline_access");

            options.Events = new()
            {
                OnRedirectToIdentityProviderForSignOut = (context) =>
                {
                    var logoutUri = $"{configuration.GetValue<string>("KeycloakAuth:KC_Authority")}/protocol/openid-connect/logout";
                    var redirectUrl = context.HttpContext.BuildRedirectUrl(context.Properties.RedirectUri);
                    logoutUri += $"&post_logout_redirect_uri={redirectUrl}";

                    context.Response.Redirect(logoutUri);
                    context.HandleResponse();
                    return Task.CompletedTask;
                },
                OnRedirectToIdentityProvider = (context) =>
                {
                    //context.ProtocolMessage.SetParameter("audience", configuration.GetValue<string>("OpenIDConnectSettings:Audience"));
                    return Task.CompletedTask;
                },
            };
        });

        //services.AddAuthorization(options =>
        //{
        //    options.DefaultPolicy = new AuthorizationPolicyBuilder(CookieAuthenticationDefaults.AuthenticationScheme)
        //        .RequireAuthenticatedUser()
        //        .Build();
        //});
        services.AddAuthorizationBuilder();


        return services;
    }

    // Thanks Damien https://github.com/damienbod/bff-auth0-aspnetcore-angular/blob/main/server/Services/ApplicationBuilderExtensions.cs#L7
    public static IApplicationBuilder UseNoUnauthorizedRedirect(this IApplicationBuilder applicationBuilder, params string[] segments)
    {
        applicationBuilder.Use(async (httpContext, func) =>
        {
            if (segments.Any(s => httpContext.Request.Path.StartsWithSegments(s, StringComparison.Ordinal)))
            {
                httpContext.Request.Headers[HeaderNames.XRequestedWith] = "XMLHttpRequest";
            }

            await func();
        });

        return applicationBuilder;
    }

    public static string BuildRedirectUrl(this HttpContext context, string? redirectUrl)
    {
        if (string.IsNullOrEmpty(redirectUrl))
        {
            redirectUrl = "/";
        }
        if (redirectUrl.StartsWith('/')) 
        {
            redirectUrl = context.Request.Scheme + "://" + context.Request.Host + context.Request.PathBase + redirectUrl;
        }
        return redirectUrl;
    }
}