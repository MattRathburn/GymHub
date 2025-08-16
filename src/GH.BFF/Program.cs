using Duende.IdentityServer.Test;
using Duende.AspNetCore.Authentication.JwtBearer.DPoP;
using Duende.IdentityServer.Services;
using Duende.IdentityServer.Validation;
using GH.BFF;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.

builder.Services.AddRazorPages();
builder.Services.AddControllers();

builder.Services.AddIdentityServer(options =>
{
    options.Events.RaiseErrorEvents = true;
    options.Events.RaiseFailureEvents = true;
    options.Events.RaiseSuccessEvents = true;
    options.Events.RaiseInformationEvents = true;
})
.AddInMemoryApiScopes(Config.ApiScopes)
.AddInMemoryIdentityResources(Config.IdentityResources)
.AddInMemoryApiResources(Config.ApiResources)
.AddInMemoryClients(Config.Clients)
.AddTestUsers(TestUsers.Users)
.AddJwtBearerClientAuthentication();

builder.Services.AddAuthentication()
    .AddLocalApi()
    .AddJwtBearer("dpop", options =>
    {
        options.Authority = "";
        options.TokenValidationParameters.ValidateAudience = false;
        options.MapInboundClaims = false;
        options.TokenValidationParameters.ValidTypes = new[] { "at+jwt" };
    });

builder.Services.ConfigureDPopTokensForScheme("dpop", options =>
{
    options.TokenMode = DPoPMode.DPoPOnly;
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("allow_all",
        policy => { policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod() });
});

var app = builder.Build();

app.UseCookiePolicy();
app.UseDeveloperExceptionPage();

app.UseCors("allow_all");

app.UseStaticFiles();

app.UseRouting();
app.UseIdentityServer();
app.UseAuthorization();


app.MapRazorPages()
    .RequireAuthorization();

app.MapControllers();


app.Run();

