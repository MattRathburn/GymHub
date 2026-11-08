using Microsoft.Extensions.DependencyInjection;

namespace GymHub.BuildingBlocks.Auth;

/// <summary>
/// Keycloak realm roles, emitted as a flat "roles" claim by the GH-Test-Realm client scope.
/// </summary>
public static class Roles
{
    public const string Trainer = "trainer";
    public const string Admin = "admin";
}

public static class Policies
{
    public const string Trainer = "Trainer";
    public const string Admin = "Admin";

    public static IServiceCollection AddGymHubAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(Trainer, policy => policy.RequireRole(Roles.Trainer, Roles.Admin))
            .AddPolicy(Admin, policy => policy.RequireRole(Roles.Admin));

        return services;
    }
}
