using GH.Plan.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GH.Plan.API.Extensions;

public static class Extensions
{
    public static void AddApplicationServices(this IHostApplicationBuilder builder)
    {
        builder.AddNpgsqlDbContext<PlanDbContext>("plandb", configureDbContextOptions: dbContextOptionsBuilder =>
        {
            dbContextOptionsBuilder.UseNpgsql();
        });

        builder.Services.AddAuthorization();

        builder.Services.AddAuthentication()
            .AddKeycloakJwtBearer("keycloak", realm: "TestRealm", options =>
            {
                options.RequireHttpsMetadata = false;
                options.Audience = "plan.api";
            });

        builder.Services.AddSwaggerGen();
    }
}
