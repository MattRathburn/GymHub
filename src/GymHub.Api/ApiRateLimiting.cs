using System.Globalization;
using System.Threading.RateLimiting;
using GymHub.ServiceDefaults;
using Microsoft.AspNetCore.RateLimiting;

namespace GymHub.Api;

/// <summary>
/// One fixed window per minute for every API request: per signed-in user (their "sub"), or per client IP for
/// anonymous calls, which get fewer. Over the limit is a 429 problem with Retry-After. The Stripe webhook and the
/// health endpoints opt out. Behind the Gateway the client IP comes from X-Forwarded-For (trusted proxies only).
/// </summary>
internal static class ApiRateLimiting
{
    public static void AddApiRateLimiting(this WebApplicationBuilder builder)
    {
        var section = builder.Configuration.GetSection("RateLimiting");
        var userPermits = section.GetValue("PermitsPerMinute", 300);
        var anonymousPermits = section.GetValue("AnonymousPermitsPerMinute", 120);

        builder.Services.AddRateLimiter(options =>
        {
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                context.User.GetUserId() is { Length: > 0 } userId
                    ? RateLimitPartition.GetFixedWindowLimiter($"user:{userId}", _ => Window(userPermits))
                    : RateLimitPartition.GetFixedWindowLimiter($"ip:{context.Connection.RemoteIpAddress}", _ => Window(anonymousPermits)));

            options.OnRejected = async (rejected, cancellationToken) =>
            {
                var response = rejected.HttpContext.Response;
                response.StatusCode = StatusCodes.Status429TooManyRequests;

                if (rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                await Results.Problem(
                        title: "Too many requests",
                        detail: "Slow down a little and try again in a minute.",
                        statusCode: StatusCodes.Status429TooManyRequests)
                    .ExecuteAsync(rejected.HttpContext);
            };
        });
    }

    private static FixedWindowRateLimiterOptions Window(int permits) => new()
    {
        PermitLimit = permits,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0
    };
}
