using GH.Plan.DbManager;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHostedService<PlanDbInitializer>();

builder.AddServiceDefaults();

builder.Services.AddDbContextPool<GH.Plan.Infrastructure.Data.PlanDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("plandb"), npgsqlOptions =>
    {
        npgsqlOptions.MigrationsAssembly("GH.Plan.DbManager");
    });
});
builder.EnrichNpgsqlDbContext<GH.Plan.Infrastructure.Data.PlanDbContext>();


var app = builder.Build();

app.Run();

