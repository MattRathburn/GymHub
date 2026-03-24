
using Asp.Versioning;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddApplicationServices();
builder.Services.AddProblemDetails();

var withApiVersioning = builder.Services.AddApiVersioning();

//var withApiVersioning = builder.Services.AddApiVersioning(options =>
//{
//    options.DefaultApiVersion = new Asp.Versioning.ApiVersion(1);
//    options.ReportApiVersions = true;
//    options.AssumeDefaultVersionWhenUnspecified = true;
//    options.ApiVersionReader = ApiVersionReader.Combine(
//        new UrlSegmentApiVersionReader(),
//        new HeaderApiVersionReader("X-Api-Version"));
//})
//    .AddApiExplorer(options =>
//    {
//        options.GroupNameFormat = "'v'V";
//        options.SubstituteApiVersionInUrl = true;
//    });

builder.AddDefaultOpenApi(withApiVersioning);

var app = builder.Build();

app.MapDefaultEndpoints();

// Enable CORS
app.UseCors();

app.NewVersionedApi("Plan")
    .MapPlanAPIv1();

app.UseDefaultOpenApi();

app.UseExceptionHandler();

app.MapGet("/api/weather", async () =>
{
    var startDate = DateOnly.FromDateTime(DateTime.Now);
    var summaries = new[] { "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching" };
    var forecasts = Enumerable.Range(1, 5).Select(index => new WeatherForecast
    {
        Date = startDate.AddDays(index),
        TemperatureC = Random.Shared.Next(-20, 55),
        Summary = summaries[Random.Shared.Next(summaries.Length)]
    }).ToArray();

    return await Task.FromResult(forecasts);
});
    //.RequireAuthorization();

//app.UseAuthentication();
//app.UseAuthorization();

app.Run();

public class WeatherForecast
{
    public DateOnly Date { get; set; }
    public int TemperatureC { get; set; }
    public string? Summary { get; set; }
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}