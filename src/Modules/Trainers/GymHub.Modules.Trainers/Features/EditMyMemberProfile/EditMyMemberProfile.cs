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

namespace GymHub.Modules.Trainers.Features.EditMyMemberProfile;

public sealed record MemberProfileDto(string UserId, string DisplayName, Uri? AvatarUrl);

public sealed record SaveMemberProfileRequest(string DisplayName);

/// <summary><paramref name="Size"/> is the file's size in bytes; the upload URL only accepts exactly that many.</summary>
public sealed record MemberAvatarUploadRequest(string FileName, string ContentType, long Size);

public sealed record SetMemberAvatarRequest(string ObjectKey);

internal sealed class SaveMemberProfileValidator : AbstractValidator<SaveMemberProfileRequest>
{
    public SaveMemberProfileValidator() =>
        RuleFor(r => r.DisplayName).NotEmpty()
            .Must(n => n.Trim().Length is >= MemberProfile.MinDisplayNameLength and <= MemberProfile.MaxDisplayNameLength)
            .WithMessage($"Display names are {MemberProfile.MinDisplayNameLength}–{MemberProfile.MaxDisplayNameLength} characters.");
}

internal sealed class MemberAvatarUploadValidator : AbstractValidator<MemberAvatarUploadRequest>
{
    public MemberAvatarUploadValidator()
    {
        RuleFor(r => r.FileName).NotEmpty().MaximumLength(200);
        RuleFor(r => r.ContentType).NotEmpty().MaximumLength(100)
            .Must(UploadRules.IsAllowedImage)
            .WithMessage($"Avatars must be {UploadRules.ImageTypesText} images.");
        RuleFor(r => r.Size)
            .Must((r, size) => UploadRules.IsAllowedSize(r.ContentType, size))
            .WithMessage(r => UploadRules.SizeLimitText(r.ContentType));
    }
}

internal sealed class SetMemberAvatarValidator : AbstractValidator<SetMemberAvatarRequest>
{
    public SetMemberAvatarValidator() => RuleFor(r => r.ObjectKey).NotEmpty().MaximumLength(300);
}

/// <summary>Any signed-in user edits their basic profile: the name and avatar shown next to their posts.</summary>
internal sealed class EditMyMemberProfileHandler(TrainersDbContext db, IObjectStorage storage, TimeProvider timeProvider)
{
    private static readonly TimeSpan UploadLifetime = TimeSpan.FromMinutes(15);

    public async Task<Result<MemberProfileDto>> Get(CurrentUser user, CancellationToken cancellationToken)
    {
        var profile = await db.MemberProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == user.Id, cancellationToken);
        return profile is null ? Error.NotFound("You haven't set up your profile yet.") : ToDto(profile);
    }

    public async Task<Result<MemberProfileDto>> Save(SaveMemberProfileRequest request, CurrentUser user, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var profile = await db.MemberProfiles.SingleOrDefaultAsync(p => p.UserId == user.Id, cancellationToken);

        if (profile is null)
        {
            var created = MemberProfile.Create(user.Id, request.DisplayName, now);

            if (!created.IsSuccess)
            {
                return created.Error!;
            }

            profile = created.Value!;
            db.MemberProfiles.Add(profile);
        }
        else
        {
            var renamed = profile.Rename(request.DisplayName, now);

            if (!renamed.IsSuccess)
            {
                return renamed.Error!;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return ToDto(profile);
    }

    public PresignedUpload RequestAvatarUpload(MemberAvatarUploadRequest request, CurrentUser user)
    {
        // The extension comes from the (allowed) type, never from the user's file name.
        var key = UploadRules.NewKey(MemberProfile.AvatarKeyPrefix(user.Id), request.ContentType);
        return storage.CreateUpload(key, request.ContentType, request.Size, UploadLifetime);
    }

    public async Task<Result<MemberProfileDto>> SetAvatar(SetMemberAvatarRequest request, CurrentUser user, CancellationToken cancellationToken)
    {
        if (!UploadRules.IsIssuedKey(request.ObjectKey, MemberProfile.AvatarKeyPrefix(user.Id)))
        {
            return Error.Forbidden("That upload doesn't belong to you.");
        }

        var profile = await db.MemberProfiles.SingleOrDefaultAsync(p => p.UserId == user.Id, cancellationToken);

        if (profile is null)
        {
            return Error.NotFound("Save your profile before adding an avatar.");
        }

        // The stored object, not the request, decides: its real type and size. The current avatar is never deleted.
        var inUse = profile.AvatarObjectKey == request.ObjectKey;
        if (await storage.CheckUploadAsync(request.ObjectKey, imagesOnly: true, inUse, cancellationToken) is { IsSuccess: false } invalid)
        {
            return invalid.Error!;
        }

        profile.SetAvatar(request.ObjectKey, timeProvider.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(profile);
    }

    private MemberProfileDto ToDto(MemberProfile p) => new(p.UserId, p.DisplayName, storage.AvatarUrl(p.AvatarObjectKey));
}

internal static class EditMyMemberProfileEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var me = app.MapGroup("/me/member-profile").RequireAuthorization();

        me.MapGet("/", async (ClaimsPrincipal user, EditMyMemberProfileHandler handler, CancellationToken ct) =>
                (await handler.Get(CurrentUser.Required(user), ct)).ToHttp())
            .WithName("GetMyMemberProfile");

        me.MapPut("/", async (SaveMemberProfileRequest request, ClaimsPrincipal user, EditMyMemberProfileHandler handler, CancellationToken ct) =>
                (await handler.Save(request, CurrentUser.Required(user), ct)).ToHttp())
            .WithValidation<SaveMemberProfileRequest>()
            .WithName("SaveMyMemberProfile");

        me.MapPost("/avatar/uploads", (MemberAvatarUploadRequest request, ClaimsPrincipal user, EditMyMemberProfileHandler handler) =>
                TypedResults.Ok(handler.RequestAvatarUpload(request, CurrentUser.Required(user))))
            .WithValidation<MemberAvatarUploadRequest>()
            .WithName("RequestMemberAvatarUpload");

        me.MapPut("/avatar", async (SetMemberAvatarRequest request, ClaimsPrincipal user, EditMyMemberProfileHandler handler, CancellationToken ct) =>
                (await handler.SetAvatar(request, CurrentUser.Required(user), ct)).ToHttp())
            .WithValidation<SetMemberAvatarRequest>()
            .WithName("SetMemberAvatar");
    }
}
