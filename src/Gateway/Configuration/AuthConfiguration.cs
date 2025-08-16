namespace Gateway.Configuration;

public static class AuthConfiguration
{
    public static IHostApplicationBuilder AddAuthConfiguration(this IHostApplicationBuilder builder)
    {
        builder.Services.AddAuthentication(options =>
        {
            options.DefaultScheme = "Cookies";
            options.DefaultChallengeScheme = "oidc";
        })
        .AddCookie("Cookies")
        .AddOpenIdConnect("oidc", options =>
        {
            options.Authority = builder.Configuration["KeycloakAuth:KC_Authority"];
            options.ClientId = builder.Configuration["KeycloakAuth:KC_ClientId"];
            options.ClientSecret = builder.Configuration["KeycloakAuth:KC_ClientSecret"];
            options.ResponseType = "code";
            options.SaveTokens = true;
            options.Scope.Clear();
            options.Scope.Add("openid");
            options.Scope.Add("profile");
            options.Scope.Add("email");
        });

        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("Authenticated", policy => policy.RequireAuthenticatedUser());
            options.AddPolicy("Admin", policy => policy.RequireRole("admin"));
            options.AddPolicy("User", policy => policy.RequireRole("user"));
        });

        return builder;
    }
}
