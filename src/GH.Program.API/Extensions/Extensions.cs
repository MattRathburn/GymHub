using GH.Program.API.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GH.Program.API.Extensions;

public static class Extensions
{
    public static void AddApplicationServices(this IHostApplicationBuilder builder)
    {
        builder.AddNpgsqlDbContext<ProgramDbContext>("programdb", configureDbContextOptions: dbContextOptionsBuilder =>
        {
            dbContextOptionsBuilder.UseNpgsql();
        });

        builder.Services.AddAuthorization();

        builder.Services.AddAuthentication()
            .AddKeycloakJwtBearer("keycloak", realm: "TestRealm", options =>
            {
                options.RequireHttpsMetadata = false;
                options.Audience = "account";
            });


    }
}
