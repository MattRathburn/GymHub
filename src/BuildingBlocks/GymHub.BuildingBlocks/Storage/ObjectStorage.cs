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
}

internal sealed class S3ObjectStorage(IAmazonS3 s3, string bucket, TimeProvider timeProvider) : IObjectStorage
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
            Expires = expiresAt.UtcDateTime
        });

        return new PresignedUpload(objectKey, new Uri(url), expiresAt);
    }

    public Uri CreateDownloadUrl(string objectKey, TimeSpan lifetime) =>
        new(s3.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = bucket,
            Key = objectKey,
            Verb = HttpVerb.GET,
            Expires = timeProvider.GetUtcNow().Add(lifetime).UtcDateTime
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

        var s3 = new AmazonS3Client(
            (string)parts["AccessKey"],
            (string)parts["SecretKey"],
            new AmazonS3Config
            {
                ServiceURL = (string)parts["Endpoint"],
                ForcePathStyle = true,
                AuthenticationRegion = "us-east-1"
            });

        builder.Services.AddSingleton<IAmazonS3>(s3);
        builder.Services.AddSingleton<IObjectStorage>(sp => new S3ObjectStorage(s3, BucketName, sp.GetRequiredService<TimeProvider>()));
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
