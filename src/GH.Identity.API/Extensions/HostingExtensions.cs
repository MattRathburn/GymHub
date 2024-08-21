using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Mappers;

namespace GH.Identity.API.Extensions;

public static class HostingExtensions
{
    public static void InitializeDatabase(IApplicationBuilder app, IConfiguration configuration)
    {
        using (var serviceScope = app.ApplicationServices.GetService<IServiceScopeFactory>()!.CreateScope())
        {
            serviceScope.ServiceProvider.GetRequiredService<PersistedGrantDbContext>().Database.Migrate();

            var context = serviceScope.ServiceProvider.GetRequiredService<ConfigurationDbContext>();
            context.Database.Migrate();
            if (context.Clients.Any())
            {
                context.Clients.RemoveRange();
            }                   

            if (!context.Clients.Any())
            {
                var clients = Config.GetClients(configuration);
                foreach (var client in clients)
                {
                    context.Clients.Add(client.ToEntity());
                }
                context.SaveChanges();
            }

            if (!context.IdentityResources.Any())
            {
                var resources = Config.GetResources();
                foreach (var resource in resources)
                {
                    context.IdentityResources.Add(resource.ToEntity());
                }
                context.SaveChanges();
            }

            if (!context.ApiScopes.Any())
            {
                var apiScopes = Config.GetApiScopes();
                foreach (var apiScope in apiScopes)
                {
                    context.ApiScopes.Add(apiScope.ToEntity());
                }
                context.SaveChanges();
            }
        }
    }
}
