using System.Data.Common;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GymHub.BuildingBlocks.Storage;

public sealed record PresignedUpload(string ObjectKey, Uri UploadUrl, DateTimeOffset ExpiresAt);

/// <summary>
/// S3-compatible object storage for user media. Clients upload and download directly with presigned URLs.
/// </summary>
public interface IObjectStorage
{
    PresignedUpload CreateUpload(string objectKey, string contentType, TimeSpan lifetime);

    Uri CreateDownloadUrl(string objectKey, TimeSpan lifetime);

    Task<bool> ExistsAsync(string objectKey, CancellationToken cancellationToken);

    /// <summary>Deletes every object whose key starts with the prefix. Deleting nothing is fine.</summary>
    Task DeletePrefixAsync(string prefix, CancellationToken cancellationToken);
}

internal sealed class S3ObjectStorage(IAmazonS3 s3, string bucket, Protocol protocol, TimeProvider timeProvider) : IObjectStorage
{
    public PresignedUpload CreateUpload(string objectKey, string contentType, TimeSpan lifetime)
    {
        var expiresAt = timeProvider.GetUtcNow().Add(lifetime);

        var url = s3.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = bucket,
            Key = objectKey,
            Verb = HttpVerb.PUT,
            ContentType = contentType,
            Expires = expiresAt.UtcDateTime,
            Protocol = protocol
        });

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

    public async Task<bool> ExistsAsync(string objectKey, CancellationToken cancellationToken)
    {
        try
        {
            await s3.GetObjectMetadataAsync(bucket, objectKey, cancellationToken);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
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
    public const string BucketName = "gymhub-media";

    /// <summary>
    /// Registers <see cref="IObjectStorage"/> from the Aspire "media" connection string
    /// (Endpoint=...;AccessKey=...;SecretKey=...), and creates the bucket at startup if it is missing.
    /// </summary>
    public static IHostApplicationBuilder AddObjectStorage(this IHostApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString(ConnectionName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionName}' is not configured.");

        var parts = new DbConnectionStringBuilder { ConnectionString = connectionString };
        var endpoint = new Uri((string)parts["Endpoint"]);

        // Presigned URLs default to https; keep the endpoint's own scheme so local http storage works.
        var protocol = endpoint.Scheme == Uri.UriSchemeHttp ? Protocol.HTTP : Protocol.HTTPS;

        var s3 = new AmazonS3Client(
            (string)parts["AccessKey"],
            (string)parts["SecretKey"],
            new AmazonS3Config
            {
                ServiceURL = endpoint.ToString(),
                // Local S3 emulators (RustFS) don't do virtual-host-style bucket addressing.
                ForcePathStyle = true,
                AuthenticationRegion = "us-east-1"
            });

        builder.Services.AddSingleton<IAmazonS3>(s3);
        builder.Services.AddSingleton<IObjectStorage>(sp => new S3ObjectStorage(s3, BucketName, protocol, sp.GetRequiredService<TimeProvider>()));
        builder.Services.AddHostedService<ObjectStorageInitializer>();

        return builder;
    }
}

/// <summary>Creates the media bucket at startup if it is missing.</summary>
public sealed class ObjectStorageInitializer(IAmazonS3 s3, ILogger<ObjectStorageInitializer> logger) : IHostedService
{
    private const string BucketName = ObjectStorageExtensions.BucketName;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var buckets = await s3.ListBucketsAsync(cancellationToken);

        if (buckets.Buckets?.Any(b => b.BucketName == BucketName) == true)
        {
            return;
        }

        await s3.PutBucketAsync(BucketName, cancellationToken);
        logger.LogInformation("Created object storage bucket {Bucket}", BucketName);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
