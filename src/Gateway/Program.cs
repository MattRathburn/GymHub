using Gateway;
using Gateway.Configuration;
using Gateway.UserModule;
using Microsoft.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddReverseProxy(builder.Configuration);
builder.Services.AddAuthenticationSchemes(builder.Configuration);

builder.Services.AddDistributedMemoryCache();
builder.Services.AddOpenIdConnectAccessTokenManagement();

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
    options.Cookie.SameSite = SameSiteMode.Strict;
});

builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseStatusCodePages();
app.UseExceptionHandler();
app.UseAntiforgery();

app.UseNoUnauthorizedRedirect("/api");
app.UseAuthentication();
app.UseAuthorization();

app.MapGroup("bff")
    .MapUserEndpoints();

app.MapReverseProxy();

app.Run();

//var builder = WebApplication.CreateBuilder(args);

//builder.AddServiceDefaults();
//builder.AddAuthConfiguration();

//builder.Services
//    .AddReverseProxy()
//    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
//    .AddServiceDiscoveryDestinationResolver();

//var app = builder.Build();

//app.UseAuthentication();
//app.UseAuthorization();

//app.MapReverseProxy();

//app.UseStatusCodePages(async context =>
//{
//    var req = context.HttpContext.Request;
//    var res = context.HttpContext.Response;

//    if (res.StatusCode == StatusCodes.Status404NotFound)
//    {
//        res.Redirect("/not-found");
//    }
//    else if(res.StatusCode == StatusCodes.Status401Unauthorized)
//    {
//        res.Redirect("http://localhost:8080/realms/GH-Test-Realm/account/");
//    }


//});

//app.Use(async (context, next) =>
//{
//    if (context.Request.Path.StartsWithSegments("/api") && !context.User.Identity.IsAuthenticated)
//    {
//        await context.ChallengeAsync("oidc");
//        return;
//    }
//    await next();
//});

//app.Run();
