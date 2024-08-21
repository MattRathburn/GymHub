
using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.Test;
using GH.Identity.API;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddControllersWithViews();

// builder.AddNpgsqlDbContext<ApplicationDbContext>("IdentityDb");

var migrationsAssembly = typeof(Program).Assembly.GetName().Name;
string connectionString = builder.Configuration.GetConnectionString("identitydb");

builder.AddSqlServerDbContext<ApplicationDbContext>("identitydb");


// Replace this with manual migrations.  Perhaps DBUp?
builder.Services.AddMigration<ApplicationDbContext, UsersSeed>();
builder.Services.AddMigration<PersistedGrantDbContext>();
builder.Services.AddMigration<ConfigurationDbContext>();


builder.Services.AddIdentity<ApplicationUser, IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddIdentityServer(options =>
{
    options.Authentication.CookieLifetime = TimeSpan.FromHours(2);

    options.Events.RaiseErrorEvents = true;
    options.Events.RaiseInformationEvents = true;
    options.Events.RaiseFailureEvents = true;
    options.Events.RaiseSuccessEvents = true;

    // TODO: Remove this line in production.
    options.KeyManagement.Enabled = false;
})
.AddConfigurationStore(options =>
{
    options.ConfigureDbContext = b => b.UseSqlServer(connectionString, 
        sql => sql.MigrationsAssembly(migrationsAssembly));
})
.AddOperationalStore(options =>
{
    options.ConfigureDbContext = b => b.UseSqlServer(connectionString,
        sql => sql.MigrationsAssembly(migrationsAssembly));
})
.AddAspNetIdentity<ApplicationUser>()
// TODO: Not recommended for production - you need to store your key material somewhere secure
.AddDeveloperSigningCredential();

builder.Services.AddTransient<IProfileService, ProfileService>();
builder.Services.AddTransient<ILoginService<ApplicationUser>, EFLoginService>();
builder.Services.AddTransient<IRedirectService, RedirectService>();

var app = builder.Build();

HostingExtensions.InitializeDatabase(app, app.Configuration);

app.MapDefaultEndpoints();

app.UseStaticFiles();

// This cookie policy fixes login issues with Chrome 80+ using HTTP
app.UseCookiePolicy(new CookiePolicyOptions { MinimumSameSitePolicy = SameSiteMode.Lax });
app.UseRouting();
app.UseIdentityServer();
app.UseAuthorization();

app.MapDefaultControllerRoute();

app.Run();

