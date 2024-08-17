using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GymHub.ServiceDefaults;

public static class AuthorizationExtensions
{
    public static IServiceCollection AddDefaultAuthorization(this IHostApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;

        var identitySection = configuration.GetSection("Identity");

        if (!identitySection.Exists())
        {
            return services;
        }

        services.AddAuthorization(options =>
        {
            var scope = identitySection.GetRequiredValue("Scope");

            options.AddPolicy(scope, policy =>
            {
                var claimType = identitySection.GetRequiredValue("ClaimType");
                var allowedValues = identitySection.GetRequiredValue("AllowedValues");

                policy.RequireAuthenticatedUser();
                policy.RequireClaim(claimType, allowedValues);
            });
        });

        return services;
    }
}
