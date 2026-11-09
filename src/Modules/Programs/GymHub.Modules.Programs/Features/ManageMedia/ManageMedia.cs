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

public sealed record RequestMediaUploadRequest(string FileName, string ContentType);

public sealed record AttachMediaRequest(MediaTarget Target, Guid? TargetId, string ObjectKey, string ContentType, string? Caption);

public sealed record AttachMediaResponse(Guid MediaId);

internal sealed class RequestMediaUploadValidator : AbstractValidator<RequestMediaUploadRequest>
{
    public RequestMediaUploadValidator()
    {
        RuleFor(r => r.FileName).NotEmpty().MaximumLength(200);
        RuleFor(r => r.ContentType).NotEmpty().MaximumLength(100)
            .Must(t => t.StartsWith("image/", StringComparison.OrdinalIgnoreCase) || t.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Only image and video uploads are supported.");
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

        var extension = Path.GetExtension(request.FileName).ToLowerInvariant();
        var key = $"{loaded.Value!.MediaKeyPrefix}{Guid.CreateVersion7():N}{extension}";

        return storage.CreateUpload(key, request.ContentType, UploadLifetime);
    }

    public async Task<Result<AttachMediaResponse>> Attach(Guid programId, AttachMediaRequest request, CurrentUser user, CancellationToken cancellationToken)
    {
        var loaded = await db.LoadOwnedAsync(programId, user, cancellationToken);

        if (!loaded.IsSuccess)
        {
            return loaded.Error!;
        }

        if (!await storage.ExistsAsync(request.ObjectKey, cancellationToken))
        {
            return Error.Invalid("The media hasn't been uploaded yet.");
        }

        var attached = loaded.Value!.AttachMedia(request.Target, request.TargetId, request.ObjectKey, request.ContentType, request.Caption);

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
