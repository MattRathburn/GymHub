using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Mappers;

namespace GH.Identity.API;

public static class PersistedGrantSeed
{
    public static async Task SeedAsync(PersistedGrantDbContext context, IApplicationBuilder app, IConfiguration configuration)
    {
        using (var serviceScope = app.ApplicationServices.GetService<IServiceScopeFactory>()!.CreateScope())
        {
            serviceScope.ServiceProvider.GetRequiredService<PersistedGrantDbContext>().Database.Migrate();

            var dbContext = serviceScope.ServiceProvider.GetRequiredService<ConfigurationDbContext>();
            dbContext.Database.Migrate();
            if (!dbContext.Clients.Any())
            {
                var clients = Config.GetClients(configuration);
                foreach (var client in clients)
                {
                    dbContext.Clients.Add(client.ToEntity());
                }
                dbContext.SaveChanges();
            }

            if (!dbContext.IdentityResources.Any())
            {
                var resources = Config.GetResources();
                foreach (var resource in resources)
                {
                    dbContext.IdentityResources.Add(resource.ToEntity());
                }
                dbContext.SaveChanges();
            }

            if (!dbContext.ApiScopes.Any())
            {
                var scopes = Config.GetApiScopes();
                foreach (var scope in scopes)
                {
                    dbContext.ApiScopes.Add(scope.ToEntity());
                }
                dbContext.SaveChanges();
            }
        }
    }
}
