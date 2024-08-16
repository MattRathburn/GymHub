using GH.Program.API.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.

builder.Services.AddAuthentication()
    .AddJwtBearer(options =>
    {
        options.Authority = "https://localhost:7258";
        options.Audience = "ProgramAPI";
        options.MapInboundClaims = true;

        // audience is optional, make sure you read the following paragraphs
        // to understand your options
        options.TokenValidationParameters.ValidateAudience = false;

        // it's recommended to check the type header to avoid "JWT confusion" attacks
        options.TokenValidationParameters.ValidTypes = new[] { "at+jwt" };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ApiScope", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("scope", "ProgramAPI");
    });
});

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.

app.UseHttpsRedirection();

app.MapGroup("/programs")
    .MapProgramAPIv1();

app.Run();