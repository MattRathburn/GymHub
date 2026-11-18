using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using EventBus.Abstractions;
using GymHub.BuildingBlocks.Identity;
using GymHub.BuildingBlocks.Outbox;
using GymHub.BuildingBlocks.Persistence;
using GymHub.BuildingBlocks.Storage;
using GymHub.Modules.Accounts.Data;
using GymHub.Modules.Billing.Data;
using GymHub.Modules.Billing.Domain;
using GymHub.Modules.Billing.Payments;
using GymHub.Modules.Discussions.Data;
using GymHub.Modules.Notifications.Data;
using GymHub.Modules.Notifications.Delivery;
using GymHub.Modules.Programs.Contracts;
using GymHub.Modules.Programs.Data;
using GymHub.Modules.Reviews.Data;
using GymHub.Modules.Subscriptions.Data;
using GymHub.Modules.Trainers.Data;
using GymHub.Modules.TrainingLog.Data;
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

    internal FakeStripeGateway Stripe { get; } = new();

    internal FakeEmailSender Email { get; } = new();

    /// <summary>Signing secret the API accepts for Stripe webhooks in tests.</summary>
    public const string WebhookSecret = "whsec_test_secret";

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();

        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ProgramsDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<SubscriptionsDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<TrainersDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<DiscussionsDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<BillingDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<TrainingLogDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<ReviewsDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<AccountsDbContext>().Database.MigrateAsync();
    }

    /// <summary>Delivers one event straight to one handler, as the outbox dispatcher would.</summary>
    public async Task HandleAsync<THandler, TEvent>(TEvent @event)
        where THandler : EventBus.Abstractions.IIntegrationEventHandler<TEvent>
        where TEvent : EventBus.Events.IntegrationEvent
    {
        using var scope = Services.CreateScope();
        await ActivatorUtilities.CreateInstance<THandler>(scope.ServiceProvider).Handle(@event);
    }

    /// <summary>Gives a trainer a Stripe account that can take charges, as finished onboarding would.</summary>
    public async Task MarkPayoutsReadyAsync(string trainerId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();

        if (await db.PayoutAccounts.SingleOrDefaultAsync(a => a.TrainerId == trainerId) is { IsReady: true })
        {
            return;
        }

        var account = new PayoutAccount(trainerId, $"acct_{Guid.NewGuid():N}", DateTime.UtcNow);
        account.Update(chargesEnabled: true, payoutsEnabled: true, detailsSubmitted: true, DateTime.UtcNow);
        db.PayoutAccounts.Add(account);
        await db.SaveChangesAsync();
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
        builder.UseSetting("Billing:StripeWebhookSecret", WebhookSecret);
        builder.UseSetting("Billing:PublicBaseUrl", "https://gymhub.test/");
        // Test clients share one (missing) IP and make many calls; RateLimitingApiTests lowers these on purpose.
        builder.UseSetting("RateLimiting:PermitsPerMinute", "100000");
        builder.UseSetting("RateLimiting:AnonymousPermitsPerMinute", "100000");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveHostedService<ObjectStorageInitializer>();
            services.RemoveHostedService<OutboxDispatcher>();
            services.AddSingleton<IObjectStorage, FakeObjectStorage>();
            services.AddSingleton<IIdentityAdmin>(IdentityAdmin);
            services.AddSingleton<IStripeGateway>(Stripe);
            services.AddSingleton<IEmailSender>(Email);

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

    /// <summary>A client whose token carries a verified email address.</summary>
    public HttpClient ClientWithEmail(string userId, string email, params string[] roles)
    {
        var client = ClientFor(userId, roles);
        client.DefaultRequestHeaders.Add(TestAuthHandler.EmailHeader, email);
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
    public const string EmailHeader = "X-Test-Email";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var user))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim> { new("sub", user.ToString()) };
        var roles = Request.Headers[RolesHeader].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries);
        claims.AddRange(roles.Select(r => new Claim("roles", r)));

        if (Request.Headers.TryGetValue(EmailHeader, out var email))
        {
            claims.Add(new Claim("email", email.ToString()));
            claims.Add(new Claim("email_verified", "true"));
        }

        var identity = new ClaimsIdentity(claims, SchemeName, "sub", "roles");
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}

public sealed class FakeObjectStorage : IObjectStorage
{
    public ConcurrentDictionary<string, StoredObject> Objects { get; } = new();

    public PresignedUpload CreateUpload(string objectKey, string contentType, long size, TimeSpan lifetime)
    {
        // Pretend the client uploads a file of the presigned type and size immediately; tests replace it to upload
        // something else.
        Objects[objectKey] = new StoredObject(size, contentType);
        return new PresignedUpload(objectKey, new Uri($"http://storage.test/{objectKey}?upload"), DateTimeOffset.UtcNow.Add(lifetime));
    }

    public Uri CreateDownloadUrl(string objectKey, TimeSpan lifetime) => new($"http://storage.test/{objectKey}");

    public Task<StoredObject?> GetInfoAsync(string objectKey, CancellationToken cancellationToken) =>
        Task.FromResult(Objects.TryGetValue(objectKey, out var stored) ? stored : null);

    public Task DeleteAsync(string objectKey, CancellationToken cancellationToken)
    {
        Objects.TryRemove(objectKey, out _);
        return Task.CompletedTask;
    }

    public Task DeletePrefixAsync(string prefix, CancellationToken cancellationToken)
    {
        foreach (var key in Objects.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)))
        {
            Objects.TryRemove(key, out _);
        }

        return Task.CompletedTask;
    }

    /// <summary>When each object was last written; objects not listed here count as written just now.</summary>
    public ConcurrentDictionary<string, DateTime> LastModified { get; } = new();

    public async IAsyncEnumerable<ListedObject> ListAsync(string prefix, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();

        foreach (var key in Objects.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
        {
            yield return new ListedObject(key, LastModified.GetValueOrDefault(key, DateTime.UtcNow));
        }
    }
}

/// <summary>Records role grants and user deletions instead of calling Keycloak.</summary>
/// <summary>An in-memory Keycloak: users added with <see cref="AddUser"/>, realm roles, enabled flags.</summary>
internal sealed class FakeIdentityAdmin : IIdentityAdmin
{
    private readonly ConcurrentDictionary<string, IdentityUser> _users = new();
    private readonly ConcurrentDictionary<(string UserId, string Role), bool> _roles = new();

    public ConcurrentBag<(string UserId, string Role)> Grants { get; } = [];

    public ConcurrentBag<string> DeletedUsers { get; } = [];

    public IdentityUser AddUser(string userId, string username, params string[] roles)
    {
        var user = new IdentityUser(userId, username, $"{username}@example.test", null, null, true, true, DateTime.UtcNow);
        _users[userId] = user;

        foreach (var role in roles)
        {
            _roles[(userId, role)] = true;
        }

        return user;
    }

    public bool IsEnabled(string userId) => _users.TryGetValue(userId, out var user) && user.Enabled;

    public bool HasRole(string userId, string role) => _roles.ContainsKey((userId, role));

    private readonly ConcurrentDictionary<(string UserId, string Role), bool> _inherited = new();

    /// <summary>A role the user gets from a group or composite role, as Keycloak would: effective, not direct.</summary>
    public void AddInheritedRole(string userId, string role) => _inherited[(userId, role)] = true;

    public Task<IReadOnlyDictionary<string, IReadOnlySet<string>>> GetRealmRolesAsync(
        IReadOnlyCollection<string> userIds, bool effective, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, IReadOnlySet<string>>>(userIds.Distinct().ToDictionary(
            id => id,
            IReadOnlySet<string> (id) => _roles.Keys.Where(k => k.UserId == id)
                .Concat(effective ? _inherited.Keys.Where(k => k.UserId == id) : [])
                .Select(k => k.Role)
                .ToHashSet()));

    public Task<IReadOnlyList<IdentityUser>> SearchUsersAsync(string? search, int first, int max, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<IdentityUser>>([.. Matching(search).Skip(first).Take(max)]);

    public Task<int> CountUsersAsync(string? search, CancellationToken cancellationToken) => Task.FromResult(Matching(search).Count());

    public Task<IReadOnlySet<string>> GetRoleMemberIdsAsync(string role, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<string>>(_roles.Keys.Where(k => k.Role == role).Select(k => k.UserId).ToHashSet());

    public Task<bool> SetEnabledAsync(string userId, bool enabled, CancellationToken cancellationToken)
    {
        if (!_users.TryGetValue(userId, out var user))
        {
            return Task.FromResult(false);
        }

        _users[userId] = user with { Enabled = enabled };
        return Task.FromResult(true);
    }

    public Task DeleteUserAsync(string userId, CancellationToken cancellationToken)
    {
        DeletedUsers.Add(userId);
        _users.TryRemove(userId, out _);
        return Task.CompletedTask;
    }

    public Task GrantRealmRoleAsync(string userId, string role, CancellationToken cancellationToken)
    {
        Grants.Add((userId, role));
        _roles[(userId, role)] = true;
        return Task.CompletedTask;
    }

    public Task RemoveRealmRoleAsync(string userId, string role, CancellationToken cancellationToken)
    {
        _roles.TryRemove((userId, role), out _);
        return Task.CompletedTask;
    }

    private IEnumerable<IdentityUser> Matching(string? search) =>
        _users.Values
            .Where(u => string.IsNullOrWhiteSpace(search)
                || u.Username.Contains(search, StringComparison.OrdinalIgnoreCase)
                || (u.Email?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderBy(u => u.Username, StringComparer.Ordinal);
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

/// <summary>
/// Records emails instead of sending them. Reports itself unconfigured so the background dispatcher stays idle;
/// tests send with EmailDispatcher.SendDueAsync.
/// </summary>
internal sealed class FakeEmailSender : IEmailSender
{
    public ConcurrentQueue<OutgoingEmail> Sent { get; } = new();

    /// <summary>Addresses whose sends throw, to exercise retries.</summary>
    public ConcurrentDictionary<string, bool> Failing { get; } = new();

    public bool IsConfigured => false;

    public Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken)
    {
        if (Failing.ContainsKey(email.To))
        {
            throw new InvalidOperationException("SMTP is down");
        }

        Sent.Enqueue(email);
        return Task.CompletedTask;
    }
}
