using GymHub.BuildingBlocks.Results;

namespace GymHub.BuildingBlocks.Storage;

/// <summary>What an object storage holds about an object: its size and the content type it was uploaded with.</summary>
public sealed record StoredObject(long Size, string? ContentType);

/// <summary>
/// The media types users may upload, with a size limit each. SVG isn't allowed (it can carry scripts). The presigned
/// upload signs the declared size and type, and <see cref="CheckUploadAsync"/> still inspects the stored object
/// before it is attached (a store that doesn't enforce the signature) and deletes it if it breaks the rules.
/// </summary>
public static class UploadRules
{
    public const long MaxImageBytes = 10L * 1024 * 1024;
    public const long MaxVideoBytes = 500L * 1024 * 1024;

    private static readonly Dictionary<string, (string Extension, long MaxBytes)> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = (".jpg", MaxImageBytes),
        ["image/png"] = (".png", MaxImageBytes),
        ["image/webp"] = (".webp", MaxImageBytes),
        ["image/gif"] = (".gif", MaxImageBytes),
        ["video/mp4"] = (".mp4", MaxVideoBytes),
        ["video/webm"] = (".webm", MaxVideoBytes)
    };

    public const string ImageTypesText = "JPEG, PNG, WebP or GIF";
    public const string MediaTypesText = "JPEG, PNG, WebP or GIF images, or MP4 or WebM video";

    public static bool IsAllowedImage(string? contentType) =>
        contentType is not null && Types.ContainsKey(contentType) && contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    public static bool IsAllowedMedia(string? contentType) => contentType is not null && Types.ContainsKey(contentType);

    /// <summary>
    /// The upload's declared size is within the type's limit. The presigned URL signs exactly this size, so storage
    /// refuses a body of any other length.
    /// </summary>
    public static bool IsAllowedSize(string? contentType, long size) =>
        size > 0 && contentType is not null && Types.TryGetValue(contentType, out var type) && size <= type.MaxBytes;

    public static string SizeLimitText(string? contentType) =>
        contentType is not null && Types.TryGetValue(contentType, out var type)
            ? $"The file is too big: the limit is {type.MaxBytes / (1024 * 1024)} MB."
            : "The file size isn't valid.";

    /// <summary>The object key's extension, from the (allowed) content type rather than the user's file name.</summary>
    public static string ExtensionFor(string contentType) => Types[contentType].Extension;

    /// <summary>A new object key under the owner's prefix: a fresh id and the type's extension, nothing from the user.</summary>
    public static string NewKey(string prefix, string contentType) => $"{prefix}{Guid.CreateVersion7():N}{ExtensionFor(contentType)}";

    /// <summary>
    /// The key has exactly the shape <see cref="NewKey"/> issues under this prefix (32 hex characters and an allowed
    /// extension). A prefix check alone would let "prefix/../elsewhere" through to a store that resolves dot segments.
    /// </summary>
    public static bool IsIssuedKey(string objectKey, string prefix)
    {
        if (!objectKey.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var name = objectKey[prefix.Length..];

        return name.Length > 32
            && !name.AsSpan(0, 32).ContainsAnyExcept("0123456789abcdef")
            && Types.Values.Any(t => string.Equals(name[32..], t.Extension, StringComparison.Ordinal));
    }

    /// <summary>
    /// Checks the uploaded object against the rules: it exists, its stored type is allowed (images only when
    /// <paramref name="imagesOnly"/>) and it isn't too big. A failing new upload is deleted; pass
    /// <paramref name="inUse"/> when the key is already attached somewhere, so a mistaken attach can't delete it.
    /// Returns the stored type.
    /// </summary>
    public static async Task<Result<string>> CheckUploadAsync(
        this IObjectStorage storage, string objectKey, bool imagesOnly, bool inUse, CancellationToken cancellationToken)
    {
        var stored = await storage.GetInfoAsync(objectKey, cancellationToken);

        if (stored is null)
        {
            return Error.Invalid("The file hasn't been uploaded yet.");
        }

        var allowed = imagesOnly ? IsAllowedImage(stored.ContentType) : IsAllowedMedia(stored.ContentType);

        if (!allowed)
        {
            if (!inUse)
            {
                await storage.DeleteAsync(objectKey, cancellationToken);
            }

            return Error.Invalid($"Upload a {(imagesOnly ? ImageTypesText : MediaTypesText)} file.");
        }

        var maxBytes = Types[stored.ContentType!].MaxBytes;

        if (stored.Size > maxBytes)
        {
            if (!inUse)
            {
                await storage.DeleteAsync(objectKey, cancellationToken);
            }

            return Error.Invalid($"The file is too big: the limit is {maxBytes / (1024 * 1024)} MB.");
        }

        return stored.ContentType!.ToLowerInvariant();
    }
}
