using Todo.API;
using GymHub.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.

builder.AddDefaultAuthentication();

builder.AddDefaultAuthorization();

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.

app.UseHttpsRedirection();

app.MapGroup("/todos")
    .ToDoGroup();

app.Run();


