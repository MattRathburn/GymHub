using System.Security.Claims;
using FluentValidation;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Results;
using GymHub.BuildingBlocks.Validation;
using GymHub.Modules.Programs.Data;
using GymHub.Modules.Programs.Features.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GymHub.Modules.Programs.Features.ManageWeeks;

public sealed record WeekRequest(string Title, DateOnly ReleasesOn);

public sealed record AddWeekResponse(Guid WeekId, int Number);

internal sealed class WeekRequestValidator : AbstractValidator<WeekRequest>
{
    public WeekRequestValidator()
    {
        RuleFor(r => r.Title).NotEmpty().MaximumLength(200);
    }
}

internal sealed class ManageWeeksHandler(ProgramsDbContext db, TimeProvider timeProvider)
{
    public async Task<Result<AddWeekResponse>> Add(Guid programId, WeekRequest request, CurrentUser user, CancellationToken cancellationToken)
    {
        var loaded = await db.LoadOwnedAsync(programId, user, cancellationToken);

        if (!loaded.IsSuccess)
        {
            return loaded.Error!;
        }

        var added = loaded.Value!.AddWeek(request.Title, request.ReleasesOn);

        if (!added.IsSuccess)
        {
            return added.Error!;
        }

        await db.SaveChangesAsync(cancellationToken);
        return new AddWeekResponse(added.Value!.Id, added.Value.Number);
    }

    public Task<Result> Update(Guid programId, Guid weekId, WeekRequest request, CurrentUser user, CancellationToken cancellationToken) =>
        Change(programId, user, p => p.UpdateWeek(weekId, request.Title, request.ReleasesOn), cancellationToken);

    public Task<Result> Remove(Guid programId, Guid weekId, CurrentUser user, CancellationToken cancellationToken) =>
        Change(programId, user, p => p.RemoveWeek(weekId, DateOnly.FromDateTime(timeProvider.UtcNow())), cancellationToken);

    private async Task<Result> Change(Guid programId, CurrentUser user, Func<Domain.TrainingProgram, Result> change, CancellationToken cancellationToken)
    {
        var loaded = await db.LoadOwnedAsync(programId, user, cancellationToken);

        if (!loaded.IsSuccess)
        {
            return loaded.Error!;
        }

        var result = change(loaded.Value!);

        if (result.IsSuccess)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}

internal static class ManageWeeksEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var weeks = app.MapGroup("/{programId:guid}/weeks").RequireAuthorization(Policies.Trainer);

        weeks.MapPost("/", async (Guid programId, WeekRequest request, ClaimsPrincipal user, ManageWeeksHandler handler, CancellationToken ct) =>
                (await handler.Add(programId, request, CurrentUser.Required(user), ct))
                    .ToHttp(r => TypedResults.Created($"/api/v1/programs/{programId}/weeks/{r.WeekId}", r)))
            .WithValidation<WeekRequest>()
            .WithName("AddWeek");

        weeks.MapPut("/{weekId:guid}", async (Guid programId, Guid weekId, WeekRequest request, ClaimsPrincipal user, ManageWeeksHandler handler, CancellationToken ct) =>
                (await handler.Update(programId, weekId, request, CurrentUser.Required(user), ct)).ToHttp())
            .WithValidation<WeekRequest>()
            .WithName("UpdateWeek");

        weeks.MapDelete("/{weekId:guid}", async (Guid programId, Guid weekId, ClaimsPrincipal user, ManageWeeksHandler handler, CancellationToken ct) =>
                (await handler.Remove(programId, weekId, CurrentUser.Required(user), ct)).ToHttp())
            .WithName("RemoveWeek");
    }
}
