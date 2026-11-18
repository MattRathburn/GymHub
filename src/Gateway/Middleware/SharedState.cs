using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.StackExchangeRedis;
using Microsoft.Extensions.Caching.Distributed;
using StackExchange.Redis;

namespace Gateway.Middleware;

public static class SharedState
{
    /// <summary>
    /// Aspire connection name of the Gateway's own Redis. It holds users' tokens, so it isn't the API's Redis: an
    /// API instance never has its credentials.
    /// </summary>
    public const string RedisConnectionName = "gateway-redis";

    private const string KeyRingKey = "gymhub-gateway:data-protection-keys";

    /// <summary>
    /// Sessions (which hold the tokens) and the Data Protection keys (which protect the sign-in, session and
    /// antiforgery cookies) live in Redis, so any Gateway instance can serve any browser. Session values are
    /// encrypted with Data Protection, and outside Development the key ring is encrypted with a certificate
    /// (Gateway:DataProtection:Certificate, a base64 PFX, and CertificatePassword), so reading Redis alone yields
    /// neither tokens nor usable keys. Without a "gateway-redis" connection string both stay in this process: fine
    /// for one instance, but a restart signs everyone out.
    /// </summary>
    public static void AddSharedState(this WebApplicationBuilder builder)
    {
        var dataProtection = builder.Services.AddDataProtection().SetApplicationName("gymhub-gateway");

        if (string.IsNullOrEmpty(builder.Configuration.GetConnectionString(RedisConnectionName)))
        {
            builder.Services.AddDistributedMemoryCache();
            return;
        }

        builder.AddRedisDistributedCache(RedisConnectionName);
        builder.Services.AddOptions<KeyManagementOptions>()
            .Configure<IConnectionMultiplexer>((options, redis) =>
                options.XmlRepository = new RedisXmlRepository(() => redis.GetDatabase(), KeyRingKey));

        if (LoadKeyRingCertificate(builder.Configuration) is { } certificate)
        {
            dataProtection.ProtectKeysWithCertificate(certificate);
        }
        else if (!builder.Environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "Gateway:DataProtection:Certificate (a base64 PFX) is required outside Development: it encrypts the key ring kept in Redis.");
        }

        EncryptCacheValues(builder.Services);
    }

    private static X509Certificate2? LoadKeyRingCertificate(IConfiguration configuration)
    {
        var section = configuration.GetSection("Gateway:DataProtection");
        var pfx = section["Certificate"];

        return string.IsNullOrEmpty(pfx)
            ? null
            : X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(pfx), section["CertificatePassword"]);
    }

    /// <summary>Wraps the registered (Redis) cache so everything stored in it, sessions included, is encrypted.</summary>
    private static void EncryptCacheValues(IServiceCollection services)
    {
        var inner = services.Last(d => d.ServiceType == typeof(IDistributedCache));
        services.Remove(inner);

        services.AddSingleton<IDistributedCache>(sp => new ProtectedDistributedCache(
            (IDistributedCache)(inner.ImplementationInstance
                ?? inner.ImplementationFactory?.Invoke(sp)
                ?? ActivatorUtilities.CreateInstance(sp, inner.ImplementationType!)),
            sp.GetRequiredService<IDataProtectionProvider>()));
    }
}

/// <summary>A distributed cache whose values are encrypted with Data Protection before they leave the process.</summary>
internal sealed class ProtectedDistributedCache(IDistributedCache inner, IDataProtectionProvider provider) : IDistributedCache
{
    private readonly IDataProtector _protector = provider.CreateProtector("gymhub-gateway:distributed-cache");

    public byte[]? Get(string key) => Unprotect(inner.Get(key));

    public async Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Unprotect(await inner.GetAsync(key, token));

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => inner.Set(key, _protector.Protect(value), options);

    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) =>
        inner.SetAsync(key, _protector.Protect(value), options, token);

    public void Refresh(string key) => inner.Refresh(key);

    public Task RefreshAsync(string key, CancellationToken token = default) => inner.RefreshAsync(key, token);

    public void Remove(string key) => inner.Remove(key);

    public Task RemoveAsync(string key, CancellationToken token = default) => inner.RemoveAsync(key, token);

    /// <summary>A value that can't be decrypted (keys lost or rotated out) reads as a miss, like an expired entry.</summary>
    private byte[]? Unprotect(byte[]? value)
    {
        if (value is null)
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(value);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }
}
