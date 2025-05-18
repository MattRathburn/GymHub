var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

//builder.AddNpgsqlDbContext("plandb");

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.

app.UseHttpsRedirection();


app.Run();

