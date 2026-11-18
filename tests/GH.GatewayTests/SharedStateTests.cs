using Gateway.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace GH.GatewayTests;

public class SharedStateTests
{
    [Fact]
    public void Cached_values_leave_the_process_encrypted_and_read_back_whole()
    {
        var inner = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var cache = new ProtectedDistributedCache(inner, new EphemeralDataProtectionProvider());
        var session = "access_token=eyJhbGciOi.secret-refresh-token"u8.ToArray();

        cache.Set("session-1", session, new DistributedCacheEntryOptions());

        var stored = inner.Get("session-1")!;
        Assert.NotEqual(session, stored);
        Assert.DoesNotContain("secret-refresh-token", System.Text.Encoding.UTF8.GetString(stored));
        Assert.Equal(session, cache.Get("session-1"));

        // Something it can't decrypt (other keys, tampering) reads as a missing entry.
        inner.Set("session-2", [1, 2, 3], new DistributedCacheEntryOptions());
        Assert.Null(cache.Get("session-2"));
    }

    [Fact]
    public void Outside_Development_a_shared_key_ring_needs_its_certificate()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"ConnectionStrings:{SharedState.RedisConnectionName}"] = "localhost:6379,abortConnect=false"
        });

        var refused = Assert.Throws<InvalidOperationException>(builder.AddSharedState);
        Assert.Contains("Gateway:DataProtection:Certificate", refused.Message);
    }
}
