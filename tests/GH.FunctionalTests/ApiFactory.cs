using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using EventBus.Abstractions;
using GymHub.BuildingBlocks.Outbox;
using GymHub.BuildingBlocks.Persistence;
using GymHub.BuildingBlocks.Storage;
using GymHub.Modules.Programs.Contracts;
using GymHub.Modules.Programs.Data;
using GymHub.Modules.Subscriptions.Data;
using GymHub.Modules.Trainers.Data;
using GymHub.Modules.Trainers.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;

namespace GH.FunctionalTests;

/// <summary>
/// Runs GymHub.Api against a throwaway Postgres. Callers authenticate with the X-Test-User / X-Test-Roles headers.
/// Object storage is faked, and the outbox is dispatched by tests on demand.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public RecordingHandler<ProgramPublished> PublishedEvents { get; } = new();

    internal FakeIdentityAdmin IdentityAdmin { get; } = new();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();

        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ProgramsDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<SubscriptionsDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<TrainersDbContext>().Database.MigrateAsync();
    }

    public new async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting($"ConnectionStrings:{PersistenceExtensions.ConnectionName}", _postgres.GetConnectionString());
        builder.UseSetting($"ConnectionStrings:{ObjectStorageExtensions.ConnectionName}", "Endpoint=http://localhost:1;AccessKey=test;SecretKey=test");
        builder.UseSetting("services:keycloak:http:0", "http://localhost:1");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveHostedService<ObjectStorageInitializer>();
            services.RemoveHostedService<OutboxDispatcher>();
            services.AddSingleton<IObjectStorage, FakeObjectStorage>();
            services.AddSingleton<IIdentityAdmin>(IdentityAdmin);

            services.AddSingleton(PublishedEvents);
            services.AddIntegrationEventHandler<ProgramPublished, RecordingHandlerAdapter<ProgramPublished>>();

            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }

    public HttpClient ClientFor(string? userId, params string[] roles)
    {
        var client = CreateClient();

        if (userId is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId);
            client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, string.Join(',', roles));
        }

        return client;
    }

    /// <summary>Runs one outbox dispatch pass for a module, as the background dispatcher would.</summary>
    public Task<int> DispatchOutboxAsync<TContext>() where TContext : ModuleDbContext =>
        ActivatorUtilities.CreateInstance<OutboxDispatcher>(Services).DispatchBatchAsync(typeof(TContext), CancellationToken.None);

    public async Task<T> WithDbAsync<TContext, T>(Func<TContext, Task<T>> query) where TContext : DbContext
    {
        using var scope = Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<TContext>());
    }
}

internal static class ServiceCollectionTestExtensions
{
    public static void RemoveHostedService<THostedService>(this IServiceCollection services) where THostedService : IHostedService
    {
        foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(THostedService)).ToList())
        {
            services.Remove(descriptor);
        }
    }
}

public sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string UserHeader = "X-Test-User";
    public const string RolesHeader = "X-Test-Roles";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var user))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim> { new("sub", user.ToString()) };
        var roles = Request.Headers[RolesHeader].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries);
        claims.AddRange(roles.Select(r => new Claim("roles", r)));

        var identity = new ClaimsIdentity(claims, SchemeName, "sub", "roles");
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}

public sealed class FakeObjectStorage : IObjectStorage
{
    public ConcurrentDictionary<string, string> Objects { get; } = new();

    public PresignedUpload CreateUpload(string objectKey, string contentType, TimeSpan lifetime)
    {
        // Pretend the client uploads immediately.
        Objects[objectKey] = contentType;
        return new PresignedUpload(objectKey, new Uri($"http://storage.test/{objectKey}?upload"), DateTimeOffset.UtcNow.Add(lifetime));
    }

    public Uri CreateDownloadUrl(string objectKey, TimeSpan lifetime) => new($"http://storage.test/{objectKey}");

    public Task<bool> ExistsAsync(string objectKey, CancellationToken cancellationToken) => Task.FromResult(Objects.ContainsKey(objectKey));
}

/// <summary>Records role grants instead of calling Keycloak.</summary>
internal sealed class FakeIdentityAdmin : IIdentityAdmin
{
    public ConcurrentBag<(string UserId, string Role)> Grants { get; } = [];

    public Task GrantRealmRoleAsync(string userId, string role, CancellationToken cancellationToken)
    {
        Grants.Add((userId, role));
        return Task.CompletedTask;
    }
}

public sealed class RecordingHandler<TEvent>
{
    public ConcurrentQueue<TEvent> Received { get; } = new();
}

public sealed class RecordingHandlerAdapter<TEvent>(RecordingHandler<TEvent> recorder) : IIntegrationEventHandler<TEvent>
    where TEvent : EventBus.Events.IntegrationEvent
{
    public Task Handle(TEvent @event)
    {
        recorder.Received.Enqueue(@event);
        return Task.CompletedTask;
    }
}
