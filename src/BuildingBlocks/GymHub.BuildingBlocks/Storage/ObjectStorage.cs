using System.Data.Common;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GymHub.BuildingBlocks.Storage;

public sealed record PresignedUpload(string ObjectKey, Uri UploadUrl, DateTimeOffset ExpiresAt);

/// <summary>
/// S3-compatible object storage for user media. Clients upload and download directly with presigned URLs.
/// </summary>
public interface IObjectStorage
{
    /// <summary>
    /// A presigned PUT for exactly <paramref name="size"/> bytes of <paramref name="contentType"/>: both are signed,
    /// so storage refuses a body of another length or type.
    /// </summary>
    PresignedUpload CreateUpload(string objectKey, string contentType, long size, TimeSpan lifetime);

    Uri CreateDownloadUrl(string objectKey, TimeSpan lifetime);

    /// <summary>The object's size and stored content type, or null when there is no such object.</summary>
    Task<StoredObject?> GetInfoAsync(string objectKey, CancellationToken cancellationToken);

    /// <summary>Deletes one object. Deleting a missing object is fine.</summary>
    Task DeleteAsync(string objectKey, CancellationToken cancellationToken);

    /// <summary>Deletes every object whose key starts with the prefix. Deleting nothing is fine.</summary>
    Task DeletePrefixAsync(string prefix, CancellationToken cancellationToken);

    /// <summary>Every object whose key starts with the prefix, a page at a time.</summary>
    IAsyncEnumerable<ListedObject> ListAsync(string prefix, CancellationToken cancellationToken);
}

/// <summary>An object found by <see cref="IObjectStorage.ListAsync"/>; LastModified is UTC.</summary>
public sealed record ListedObject(string Key, DateTime LastModified);

internal sealed class S3ObjectStorage(IAmazonS3 s3, string bucket, Protocol protocol, TimeProvider timeProvider) : IObjectStorage
{
    public PresignedUpload CreateUpload(string objectKey, string contentType, long size, TimeSpan lifetime)
    {
        var expiresAt = timeProvider.GetUtcNow().Add(lifetime);

        var request = new GetPreSignedUrlRequest
        {
            BucketName = bucket,
            Key = objectKey,
            Verb = HttpVerb.PUT,
            ContentType = contentType,
            Expires = expiresAt.UtcDateTime,
            Protocol = protocol
        };

        // Signed (X-Amz-SignedHeaders includes content-length), so the browser's PUT must be exactly this size.
        request.Headers.ContentLength = size;
        var url = s3.GetPreSignedURL(request);

        return new PresignedUpload(objectKey, new Uri(url), expiresAt);
    }

    public Uri CreateDownloadUrl(string objectKey, TimeSpan lifetime) =>
        new(s3.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = bucket,
            Key = objectKey,
            Verb = HttpVerb.GET,
            Expires = timeProvider.GetUtcNow().Add(lifetime).UtcDateTime,
            Protocol = protocol
        }));

    public async Task<StoredObject?> GetInfoAsync(string objectKey, CancellationToken cancellationToken)
    {
        try
        {
            var metadata = await s3.GetObjectMetadataAsync(bucket, objectKey, cancellationToken);
            return new StoredObject(metadata.ContentLength, metadata.Headers.ContentType);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public Task DeleteAsync(string objectKey, CancellationToken cancellationToken) =>
        s3.DeleteObjectAsync(bucket, objectKey, cancellationToken);

    public async IAsyncEnumerable<ListedObject> ListAsync(string prefix, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var request = new ListObjectsV2Request { BucketName = bucket, Prefix = prefix };
        ListObjectsV2Response page;

        do
        {
            page = await s3.ListObjectsV2Async(request, cancellationToken);

            foreach (var item in page.S3Objects ?? [])
            {
                yield return new ListedObject(item.Key, (item.LastModified ?? DateTime.UtcNow).ToUniversalTime());
            }

            request.ContinuationToken = page.NextContinuationToken;
        }
        while (page.IsTruncated == true);
    }

    public async Task DeletePrefixAsync(string prefix, CancellationToken cancellationToken)
    {
        var request = new ListObjectsV2Request { BucketName = bucket, Prefix = prefix };
        ListObjectsV2Response page;

        do
        {
            page = await s3.ListObjectsV2Async(request, cancellationToken);

            // A listed page holds at most 1000 keys, which is also DeleteObjects' limit: one request per page.
            if (page.S3Objects is { Count: > 0 } objects)
            {
                var deleted = await s3.DeleteObjectsAsync(new DeleteObjectsRequest
                {
                    BucketName = bucket,
                    Objects = [.. objects.Select(o => new KeyVersion { Key = o.Key })],
                    Quiet = true
                }, cancellationToken);

                if (deleted.DeleteErrors is { Count: > 0 } errors)
                {
                    throw new InvalidOperationException(
                        $"Couldn't delete {errors.Count} objects under '{prefix}', e.g. {errors[0].Key}: {errors[0].Code} {errors[0].Message}");
                }
            }

            request.ContinuationToken = page.NextContinuationToken;
        }
        while (page.IsTruncated == true);
    }
}

public static class ObjectStorageExtensions
{
    public const string ConnectionName = "media";
    public const string DefaultBucketName = "gymhub-media";

    /// <summary>
    /// Registers <see cref="IObjectStorage"/> from the Aspire "media" connection string
    /// (Endpoint=...;AccessKey=...;SecretKey=...[;Bucket=...][;Region=...]), and creates the bucket at startup if it
    /// is missing. Region defaults to us-east-1; Cloudflare R2 uses "auto".
    /// </summary>
    public static IHostApplicationBuilder AddObjectStorage(this IHostApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString(ConnectionName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionName}' is not configured.");

        var parts = new DbConnectionStringBuilder { ConnectionString = connectionString };
        var endpoint = new Uri((string)parts["Endpoint"]);
        var bucket = new MediaBucket(parts.TryGetValue("Bucket", out var b) && b is string { Length: > 0 } name ? name : DefaultBucketName);
        var region = parts.TryGetValue("Region", out var r) && r is string { Length: > 0 } regionName ? regionName : "us-east-1";

        // Presigned URLs default to https; keep the endpoint's own scheme so local http storage works.
        var protocol = endpoint.Scheme == Uri.UriSchemeHttp ? Protocol.HTTP : Protocol.HTTPS;

        var s3 = new AmazonS3Client(
            (string)parts["AccessKey"],
            (string)parts["SecretKey"],
            new AmazonS3Config
            {
                ServiceURL = endpoint.ToString(),
                // Local S3 emulators (RustFS) don't do virtual-host-style bucket addressing; R2 accepts both.
                ForcePathStyle = true,
                AuthenticationRegion = region
            });

        builder.Services.AddSingleton<IAmazonS3>(s3);
        builder.Services.AddSingleton(bucket);
        builder.Services.AddSingleton<IObjectStorage>(sp => new S3ObjectStorage(s3, bucket.Name, protocol, sp.GetRequiredService<TimeProvider>()));
        builder.Services.AddHostedService<ObjectStorageInitializer>();
        builder.Services.AddHealthChecks().AddCheck<ObjectStorageHealthCheck>("object-storage");

        return builder;
    }
}

/// <summary>The bucket user media lives in.</summary>
public sealed record MediaBucket(string Name);

/// <summary>The media bucket can be listed.</summary>
public sealed class ObjectStorageHealthCheck(IAmazonS3 s3, MediaBucket bucket) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await s3.ListObjectsV2Async(new ListObjectsV2Request { BucketName = bucket.Name, MaxKeys = 1 }, cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is AmazonServiceException or HttpRequestException or TaskCanceledException)
        {
            return HealthCheckResult.Unhealthy("Object storage is unreachable.", ex);
        }
    }
}

/// <summary>
/// Creates the media bucket at startup if it is missing. Checks just that bucket (HeadBucket), so a token scoped to
/// one bucket, as R2 issues them, is enough.
/// </summary>
public sealed class ObjectStorageInitializer(IAmazonS3 s3, MediaBucket bucket, ILogger<ObjectStorageInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await s3.HeadBucketAsync(new HeadBucketRequest { BucketName = bucket.Name }, cancellationToken);
            return;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
        }

        await s3.PutBucketAsync(bucket.Name, cancellationToken);
        logger.LogInformation("Created object storage bucket {Bucket}", bucket.Name);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
