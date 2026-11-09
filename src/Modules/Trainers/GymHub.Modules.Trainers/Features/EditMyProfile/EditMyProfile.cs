using System.Security.Claims;
using FluentValidation;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Results;
using GymHub.BuildingBlocks.Storage;
using GymHub.BuildingBlocks.Validation;
using GymHub.Modules.Trainers.Data;
using GymHub.Modules.Trainers.Domain;
using GymHub.Modules.Trainers.Features.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Trainers.Features.EditMyProfile;

public sealed record MyProfileDto(string UserId, string DisplayName, string? Bio, IReadOnlyList<string> Specialties, IReadOnlyList<string> Links, Uri? AvatarUrl);

public sealed record RequestAvatarUploadRequest(string FileName, string ContentType);

public sealed record SetAvatarRequest(string ObjectKey);

internal sealed class RequestAvatarUploadValidator : AbstractValidator<RequestAvatarUploadRequest>
{
    public RequestAvatarUploadValidator()
    {
        RuleFor(r => r.FileName).NotEmpty().MaximumLength(200);
        RuleFor(r => r.ContentType).NotEmpty().MaximumLength(100)
            .Must(t => t.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Avatars must be images.");
    }
}

internal sealed class SetAvatarValidator : AbstractValidator<SetAvatarRequest>
{
    public SetAvatarValidator() => RuleFor(r => r.ObjectKey).NotEmpty().MaximumLength(300);
}

/// <summary>
/// A trainer edits their own profile. Saving creates it if missing, so trainers who never applied (seeded or
/// granted the role directly in Keycloak) can still have one. Avatars use the two-step presigned upload.
/// </summary>
internal sealed class EditMyProfileHandler(TrainersDbContext db, IObjectStorage storage, TimeProvider timeProvider)
{
    private static readonly TimeSpan UploadLifetime = TimeSpan.FromMinutes(15);

    public async Task<Result<MyProfileDto>> Get(CurrentUser user, CancellationToken cancellationToken)
    {
        var profile = await db.Profiles.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == user.Id, cancellationToken);
        return profile is null ? Error.NotFound("You don't have a profile yet.") : ToDto(profile);
    }

    public async Task<Result<MyProfileDto>> Save(TrainerDetailsRequest request, CurrentUser user, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var profile = await db.Profiles.SingleOrDefaultAsync(p => p.UserId == user.Id, cancellationToken);

        if (profile is null)
        {
            profile = TrainerProfile.Create(user.Id, request.ToDetails(), now);
            db.Profiles.Add(profile);
        }
        else
        {
            profile.Update(request.ToDetails(), now);
        }

        await db.SaveChangesAsync(cancellationToken);
        return ToDto(profile);
    }

    public PresignedUpload RequestAvatarUpload(RequestAvatarUploadRequest request, CurrentUser user)
    {
        var extension = Path.GetExtension(request.FileName).ToLowerInvariant();
        var key = $"{TrainerProfile.AvatarKeyPrefix(user.Id)}{Guid.CreateVersion7():N}{extension}";
        return storage.CreateUpload(key, request.ContentType, UploadLifetime);
    }

    public async Task<Result<MyProfileDto>> SetAvatar(SetAvatarRequest request, CurrentUser user, CancellationToken cancellationToken)
    {
        if (!request.ObjectKey.StartsWith(TrainerProfile.AvatarKeyPrefix(user.Id), StringComparison.Ordinal))
        {
            return Error.Forbidden("That upload doesn't belong to you.");
        }

        var profile = await db.Profiles.SingleOrDefaultAsync(p => p.UserId == user.Id, cancellationToken);

        if (profile is null)
        {
            return Error.NotFound("Save your profile before adding an avatar.");
        }

        if (!await storage.ExistsAsync(request.ObjectKey, cancellationToken))
        {
            return Error.Invalid("The avatar hasn't been uploaded yet.");
        }

        profile.SetAvatar(request.ObjectKey, timeProvider.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(profile);
    }

    private MyProfileDto ToDto(TrainerProfile p) =>
        new(p.UserId, p.DisplayName, p.Bio, p.Specialties, p.Links, storage.AvatarUrl(p.AvatarObjectKey));
}

internal static class EditMyProfileEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var me = app.MapGroup("/me").RequireAuthorization(Policies.Trainer);

        me.MapGet("/profile", async (ClaimsPrincipal user, EditMyProfileHandler handler, CancellationToken ct) =>
                (await handler.Get(CurrentUser.Required(user), ct)).ToHttp())
            .WithName("GetMyTrainerProfile");

        me.MapPut("/profile", async (TrainerDetailsRequest request, ClaimsPrincipal user, EditMyProfileHandler handler, CancellationToken ct) =>
                (await handler.Save(request, CurrentUser.Required(user), ct)).ToHttp())
            .WithValidation<TrainerDetailsRequest>()
            .WithName("SaveMyTrainerProfile");

        me.MapPost("/avatar/uploads", (RequestAvatarUploadRequest request, ClaimsPrincipal user, EditMyProfileHandler handler) =>
                TypedResults.Ok(handler.RequestAvatarUpload(request, CurrentUser.Required(user))))
            .WithValidation<RequestAvatarUploadRequest>()
            .WithName("RequestAvatarUpload");

        me.MapPut("/avatar", async (SetAvatarRequest request, ClaimsPrincipal user, EditMyProfileHandler handler, CancellationToken ct) =>
                (await handler.SetAvatar(request, CurrentUser.Required(user), ct)).ToHttp())
            .WithValidation<SetAvatarRequest>()
            .WithName("SetAvatar");
    }
}
