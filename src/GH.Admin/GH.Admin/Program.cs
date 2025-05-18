using GH.Admin.Client.Pages;
using GH.Admin.Components;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

//app.MapGet("/weather", async () =>
//{
//    var startDate = DateOnly.FromDateTime(DateTime.Now);
//    var summaries = new[] { "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching" };
//    var forecasts = Enumerable.Range(1, 5).Select(index => new WeatherForecast
//    {
//        Date = startDate.AddDays(index),
//        TemperatureC = Random.Shared.Next(-20, 55),
//        Summary = summaries[Random.Shared.Next(summaries.Length)]
//    }).ToArray();

//    return await Task.FromResult(forecasts);
//}).RequireAuthorization();


app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(GH.Admin.Client._Imports).Assembly);

app.Run();

//public class WeatherForecast
//{
//    public DateOnly Date { get; set; }
//    public int TemperatureC { get; set; }
//    public string? Summary { get; set; }
//    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
//}