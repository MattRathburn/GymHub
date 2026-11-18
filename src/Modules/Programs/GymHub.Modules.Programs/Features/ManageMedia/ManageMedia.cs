using System.Security.Claims;
using FluentValidation;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Results;
using GymHub.BuildingBlocks.Storage;
using GymHub.BuildingBlocks.Validation;
using GymHub.Modules.Programs.Data;
using GymHub.Modules.Programs.Domain;
using GymHub.Modules.Programs.Features.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GymHub.Modules.Programs.Features.ManageMedia;

/// <summary><paramref name="Size"/> is the file's size in bytes; the upload URL only accepts exactly that many.</summary>
public sealed record RequestMediaUploadRequest(string FileName, string ContentType, long Size);

public sealed record AttachMediaRequest(MediaTarget Target, Guid? TargetId, string ObjectKey, string ContentType, string? Caption);

public sealed record AttachMediaResponse(Guid MediaId);

internal sealed class RequestMediaUploadValidator : AbstractValidator<RequestMediaUploadRequest>
{
    public RequestMediaUploadValidator()
    {
        RuleFor(r => r.FileName).NotEmpty().MaximumLength(200);
        RuleFor(r => r.ContentType).NotEmpty().MaximumLength(100)
            .Must(UploadRules.IsAllowedMedia)
            .WithMessage($"Upload {UploadRules.MediaTypesText}.");
        RuleFor(r => r.Size)
            .Must((r, size) => UploadRules.IsAllowedSize(r.ContentType, size))
            .WithMessage(r => UploadRules.SizeLimitText(r.ContentType));
    }
}

internal sealed class AttachMediaValidator : AbstractValidator<AttachMediaRequest>
{
    public AttachMediaValidator()
    {
        RuleFor(r => r.Target).IsInEnum();
        RuleFor(r => r.TargetId).NotNull().When(r => r.Target is not (MediaTarget.Cover or MediaTarget.Program));
        RuleFor(r => r.ObjectKey).NotEmpty().MaximumLength(300);
        RuleFor(r => r.ContentType).NotEmpty().MaximumLength(100);
        RuleFor(r => r.Caption).MaximumLength(300);
    }
}

/// <summary>
/// Two-step upload: the trainer gets a presigned URL, uploads straight to object storage, then attaches the object.
/// </summary>
internal sealed class ManageMediaHandler(ProgramsDbContext db, IObjectStorage storage)
{
    private static readonly TimeSpan UploadLifetime = TimeSpan.FromMinutes(15);

    public async Task<Result<PresignedUpload>> RequestUpload(Guid programId, RequestMediaUploadRequest request, CurrentUser user, CancellationToken cancellationToken)
    {
        var loaded = await db.LoadOwnedAsync(programId, user, cancellationToken);

        if (!loaded.IsSuccess)
        {
            return loaded.Error!;
        }

        // The extension comes from the (allowed) type, never from the user's file name.
        var key = UploadRules.NewKey(loaded.Value!.MediaKeyPrefix, request.ContentType);

        return storage.CreateUpload(key, request.ContentType, request.Size, UploadLifetime);
    }

    public async Task<Result<AttachMediaResponse>> Attach(Guid programId, AttachMediaRequest request, CurrentUser user, CancellationToken cancellationToken)
    {
        var loaded = await db.LoadOwnedAsync(programId, user, cancellationToken);

        if (!loaded.IsSuccess)
        {
            return loaded.Error!;
        }

        // Only keys this program's upload endpoint issues (prefix, id and an allowed extension) can be checked, and deleted.
        if (!UploadRules.IsIssuedKey(request.ObjectKey, loaded.Value!.MediaKeyPrefix))
        {
            return Error.Invalid("The media object doesn't belong to this program.");
        }

        // The stored object's real type and size decide, not the type the request claims. An object already attached
        // in this program (re-used, or picked by mistake) is checked but never deleted.
        var program = loaded.Value!;
        var inUse = program.CoverObjectKey == request.ObjectKey || program.Media.Any(m => m.ObjectKey == request.ObjectKey);
        var checkedType = await storage.CheckUploadAsync(
            request.ObjectKey, imagesOnly: request.Target == MediaTarget.Cover, inUse, cancellationToken);

        if (!checkedType.IsSuccess)
        {
            return checkedType.Error!;
        }

        var attached = loaded.Value!.AttachMedia(request.Target, request.TargetId, request.ObjectKey, checkedType.Value!, request.Caption);

        if (!attached.IsSuccess)
        {
            return attached.Error!;
        }

        await db.SaveChangesAsync(cancellationToken);
        return new AttachMediaResponse(attached.Value!.Id);
    }
}

internal static class ManageMediaEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var media = app.MapGroup("/{programId:guid}/media").RequireAuthorization(Policies.Trainer);

        media.MapPost("/uploads", async (Guid programId, RequestMediaUploadRequest request, ClaimsPrincipal user, ManageMediaHandler handler, CancellationToken ct) =>
                (await handler.RequestUpload(programId, request, CurrentUser.Required(user), ct)).ToHttp())
            .WithValidation<RequestMediaUploadRequest>()
            .WithName("RequestMediaUpload");

        media.MapPost("/", async (Guid programId, AttachMediaRequest request, ClaimsPrincipal user, ManageMediaHandler handler, CancellationToken ct) =>
                (await handler.Attach(programId, request, CurrentUser.Required(user), ct))
                    .ToHttp(r => TypedResults.Created($"/api/v1/programs/{programId}/media/{r.MediaId}", r)))
            .WithValidation<AttachMediaRequest>()
            .WithName("AttachMedia");
    }
}
