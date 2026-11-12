using System.Security.Claims;
using FluentValidation;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Results;
using GymHub.BuildingBlocks.Validation;
using GymHub.Modules.Notifications.Data;
using GymHub.Modules.Notifications.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Notifications.Features.Preferences;

public sealed record PreferenceDto(NotificationType Type, NotificationGroup Group, string Label, bool InApp, bool Email, bool EmailLocked);

public sealed record PreferenceChange(NotificationType Type, bool InApp, bool Email);

public sealed record UpdatePreferencesRequest(IReadOnlyList<PreferenceChange> Preferences);

internal sealed class UpdatePreferencesValidator : AbstractValidator<UpdatePreferencesRequest>
{
    public UpdatePreferencesValidator()
    {
        RuleFor(r => r.Preferences).NotNull();
        RuleForEach(r => r.Preferences).ChildRules(change => change.RuleFor(c => c.Type).IsInEnum());
    }
}

/// <summary>Per type, an in-app and an email switch. Billing and access emails are locked on.</summary>
internal sealed class PreferencesHandler(NotificationsDbContext db)
{
    public async Task<IReadOnlyList<PreferenceDto>> Get(CurrentUser user, CancellationToken cancellationToken)
    {
        var saved = await db.Preferences.AsNoTracking().Where(p => p.UserId == user.Id).ToListAsync(cancellationToken);

        return [.. NotificationCatalog.All.Select(definition =>
        {
            var (inApp, email) = NotificationCatalog.Resolve(definition.Type, saved.SingleOrDefault(p => p.Type == definition.Type));
            return new PreferenceDto(definition.Type, definition.Group, definition.Label, inApp, email, definition.EmailLocked);
        })];
    }

    public async Task<Result> Update(CurrentUser user, UpdatePreferencesRequest request, CancellationToken cancellationToken)
    {
        if (request.Preferences.FirstOrDefault(c => !c.Email && NotificationCatalog.Get(c.Type).EmailLocked) is { } locked)
        {
            return Error.Invalid($"\"{NotificationCatalog.Get(locked.Type).Label}\" emails can't be turned off.");
        }

        var saved = await db.Preferences.Where(p => p.UserId == user.Id).ToListAsync(cancellationToken);

        foreach (var change in request.Preferences.DistinctBy(c => c.Type))
        {
            var existing = saved.SingleOrDefault(p => p.Type == change.Type);

            if (existing is null)
            {
                db.Preferences.Add(new NotificationPreference(user.Id, change.Type, change.InApp, change.Email));
            }
            else
            {
                existing.Set(change.InApp, change.Email);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal static class PreferencesEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/preferences", async (ClaimsPrincipal user, PreferencesHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.Get(CurrentUser.Required(user), ct)))
            .RequireAuthorization()
            .WithName("GetNotificationPreferences");

        app.MapPut("/preferences", async (UpdatePreferencesRequest request, ClaimsPrincipal user, PreferencesHandler handler, CancellationToken ct) =>
                (await handler.Update(CurrentUser.Required(user), request, ct)).ToHttp())
            .RequireAuthorization()
            .WithValidation<UpdatePreferencesRequest>()
            .WithName("UpdateNotificationPreferences");
    }
}
