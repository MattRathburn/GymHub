using Gateway.Configuration;
using Gateway.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using System.Threading.RateLimiting;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Gateway.Middleware;

public static class GatewaySetup
{
    private static readonly string ENV_GATEWAY_CONFIG = "GATEWAY_CONFIG";

    public static void AddConfigFiles(this WebApplicationBuilder builder)
    {
        var envConfig = Environment.GetEnvironmentVariable(ENV_GATEWAY_CONFIG);
        var cmdLineArgs = Environment.GetCommandLineArgs();

        if (cmdLineArgs != null && cmdLineArgs.Count() > 1)
        {
            builder.Configuration.AddJsonFile(cmdLineArgs[1], false, true);
        }
        else if (envConfig != null)
        {
            builder.Configuration.AddJsonFile(envConfig, false, true);
        }
    }


    private static void AddTokenExchangeService(this WebApplicationBuilder builder, GatewayConfigurationRecord config) {
        var strategy = config.TokenExchangeStrategy;
        if (string.IsNullOrEmpty(strategy)) {
            strategy = "none";
        }

        switch(strategy.ToLower()) {
            case "none":
                    builder.Services.AddSingleton<ITokenExchangeService, NullTokenExchangeService>();
            break;

            case "azuread":
                    builder.Services.AddSingleton<ITokenExchangeService, AzureAdTokenExchangeService>();
            break;

            case "default":
                    builder.Services.AddSingleton<ITokenExchangeService, TokenExchangeService>();
            break;

            default:
                throw new ArgumentException($"Unsupported TokenExchangeStrategy in config found: {config.TokenExchangeStrategy}. Possible values: none, AzureAd, default");
            
        }
    }

    public const string AuthRateLimit = "auth";

    public static void AddGateway(this WebApplicationBuilder builder, GatewayConfigurationRecord config, DiscoveryDocument disco)
    {
        builder.Services.AddReverseProxy()
            .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
            .AddServiceDiscoveryDestinationResolver();

        builder.Services.AddSingleton<DiscoveryDocument>(disco);
        builder.Services.AddSingleton<GatewayConfigurationRecord>(config);
        
        builder.Services.AddSingleton<TokenRefreshService>();
        builder.AddTokenExchangeService(config);

        builder.Services.AddSingleton<ApiTokenService>();
        builder.Services.AddSingleton<GatewayService>();
        builder.Services.AddSingleton<TokenHandler>();

        var sessionTimeoutInMin = config.SessionTimeoutInMin;
        builder.Services.AddSession(options =>
        {
            options.IdleTimeout = TimeSpan.FromMinutes(sessionTimeoutInMin);
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.HttpOnly = true;
        });

        // Sign-in, sign-up and sign-out start an OIDC round trip each; limit them per client IP.
        var authPermitsPerMinute = builder.Configuration.GetValue("RateLimiting:AuthPermitsPerMinute", 20);
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(AuthRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = authPermitsPerMinute, Window = TimeSpan.FromMinutes(1) }));
        });

        builder.Services.AddHttpClient(nameof(KeycloakHealthCheck));
        builder.Services.AddHealthChecks().AddCheck<KeycloakHealthCheck>("keycloak");

        builder.Services.AddAntiforgery(setup =>
        {
            setup.HeaderName = "X-XSRF-TOKEN";
        });

        builder.Services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        builder.Services.AddSingleton<DiscoveryService>();

        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("authPolicy", policy =>
            {
                policy.RequireAuthenticatedUser();
            });
        });

        builder.Services.AddAuthentication(options =>
        {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
        })
        .AddCookie(setup =>
        {
            setup.ExpireTimeSpan = TimeSpan.FromMinutes(sessionTimeoutInMin);
            setup.SlidingExpiration = true;
            setup.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            setup.Cookie.HttpOnly = true;
        })
        .AddOpenIdConnect(options =>
        {
            options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.Authority = config.Authority;

            // The issuer is the public authority; the metadata and the token endpoint may come from Keycloak's private
            // address (see MetadataAuthority). Metadata over plain http is accepted only when it was configured so.
            if (config.MetadataAuthority != config.Authority)
            {
                options.MetadataAddress = DiscoveryService.DiscoveryUrl(config.MetadataAuthority);
            }
            options.ClientId = config.ClientId;
            options.UsePkce = true;
            options.ClientSecret = config.ClientSecret;
            options.ResponseType = OpenIdConnectResponseType.Code;
            options.SaveTokens = false;
            options.GetClaimsFromUserInfoEndpoint = config.QueryUserInfoEndpoint;
            options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
            options.NonceCookie.SecurePolicy = CookieSecurePolicy.Always;
            options.RequireHttpsMetadata = !builder.Environment.IsDevelopment() && config.MetadataAuthority == config.Authority;

            // Keep Keycloak's claim names ("sub", "roles") instead of the WS-* URIs.
            options.MapInboundClaims = false;
            options.TokenValidationParameters.NameClaimType = "preferred_username";
            options.TokenValidationParameters.RoleClaimType = "roles";

            var scopes = config.Scopes;
            var scopeArray = scopes.Split(" ");
            foreach (var scope in scopeArray)
            {
                options.Scope.Add(scope);
            }

            options.Events.OnTokenValidated = (context) =>
            {
                var tokenHandler = context.HttpContext.RequestServices.GetRequiredService<TokenHandler>();
                tokenHandler.HandleToken(context);
                return Task.FromResult(0);
            };

            // "/signup" asks Keycloak for its registration form instead of the login form (OIDC prompt=create).
            options.Events.OnRedirectToIdentityProvider = (context) =>
            {
                if (context.Properties.Items.TryGetValue(GatewayEndpoints.PromptItem, out var prompt))
                {
                    context.ProtocolMessage.Prompt = prompt;
                }

                return Task.CompletedTask;
            };

            // Keycloak ignores a prompt sent through a pushed authorization request (PAR), so sign-up skips PAR and
            // sends its parameters in the redirect instead. Login keeps using PAR.
            options.Events.OnPushAuthorization = (context) =>
            {
                if (context.Properties.Items.ContainsKey(GatewayEndpoints.PromptItem))
                {
                    context.SkipPush();
                }

                return Task.CompletedTask;
            };

            options.Events.OnRedirectToIdentityProviderForSignOut = (context) =>
            {
                LogoutHandler.HandleLogout(context, config);
                return Task.CompletedTask;
            };
        });
    }

    private static void UseYarp(this WebApplication app)
    {
        app.MapReverseProxy(pipeline =>
        {
            pipeline.UseGatewayPipeline();
        });
    }

    public static void UseGateway(this WebApplication app)
    {
        app.UseRouting();
        app.UseRateLimiter();
        app.UseSession();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseCookiePolicy();
        app.UseXsrfCookie();
        app.UseGatewayEndpoints();
        app.UseYarp();
    }

}
