using GymHub.AppHost;

var builder = DistributedApplication.CreateBuilder(args);

//builder.AddForwardedHeaders();

//var redis = builder.AddRedis("redis");
//var rabbitMq = builder.AddRabbitMQ("eventbus");
//var postgres = builder.AddPostgres("postgres")
//    .WithImage("ankane/pgvector")
//    .WithImageTag("latest");

//var catalogDb = postgres.AddDatabase("catalogdb");
//var identityDb = postgres.AddDatabase("identitydb");
//var webhooksDb = postgres.AddDatabase("webhooksdb");

//var launchProfileName = ShouldUseHttpForEndpoints() ? "http" : "https";

//var webApp = builder.AddProject<Projects.WebApp_Server>("webapp-server");

//var identityApi = builder.AddProject<Projects.Identity_API>("identity-api", launchProfileName)
//        .WithExternalHttpEndpoints()
//        .WithReference(identityDb);

//var identityEndpoint = identityApi.GetEndpoint(launchProfileName);

//builder.AddProject<Projects.Todo_API>("todo-api")
//    .WithReference(redis)
//    .WithEnvironment("Identity__Url", identityEndpoint);

var postgres = builder.AddPostgres("postgres")
    .WithImage("ankane/pgvector")
    .WithImageTag("latest");

var sqlserver = builder.AddSqlServer("sqldb");

//var identityDb = postgres.AddDatabase("identitydb");

var identityDb = sqlserver.AddDatabase("identitydb");

builder.AddProject<Projects.WebApp_Server>("webapp-server");

builder.AddProject<Projects.GH_Identity_API>("gh-identity-api")
    .WithExternalHttpEndpoints()
    .WithReference(identityDb);

builder.AddProject<Projects.Todo_API>("todo-api")
    .WithExternalHttpEndpoints();

builder.Build().Run();


// For test use only.
// Looks for an environment variable that forces the use of HTTP for all the endpoints. We
// are doing this for ease of running the Playwright tests in CI.
static bool ShouldUseHttpForEndpoints()
{
    const string EnvVarName = "GH_USE_HTTP_ENDPOINTS";
    var envValue = Environment.GetEnvironmentVariable(EnvVarName);

    // Attempt to parse the environment variable value; return true if it's exactly "1".
    return int.TryParse(envValue, out int result) && result == 1;
}
