using System.Security.Claims;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Results;
using GymHub.BuildingBlocks.Validation;
using GymHub.Modules.TrainingLog.Data;
using GymHub.Modules.TrainingLog.Domain;
using GymHub.Modules.TrainingLog.Features.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.TrainingLog.Features.EditSession;

/// <summary>
/// Changes or deletes one of the member's sessions. Program exercises keep the names they were logged with; new
/// exercises added while editing are freestyle.
/// </summary>
internal sealed class EditSessionHandler(TrainingLogDbContext db, SessionRecords records, TimeProvider timeProvider)
{
    public async Task<Result<SavedSessionResponse>> Update(Guid id, SessionRequest request, CurrentUser user, CancellationToken cancellationToken)
    {
        var session = await LoadAsync(id, user, cancellationToken);

        if (session is null)
        {
            return Error.NotFound("Session not found.");
        }

        var programNames = session.Exercises
            .Where(e => e.ProgramExerciseId is not null)
            .GroupBy(e => e.ProgramExerciseId!.Value)
            .ToDictionary(g => g.Key, g => g.First().Name);

        var drafts = new List<LoggedExerciseDraft>(request.Exercises.Count);

        foreach (var exercise in request.Exercises)
        {
            var name = exercise.Name;

            if (exercise.ProgramExerciseId is { } programExerciseId)
            {
                if (!programNames.TryGetValue(programExerciseId, out var programName))
                {
                    return Error.Invalid($"{exercise.Name.Trim()} isn't part of this program workout.");
                }

                name = programName;
            }

            drafts.Add(new LoggedExerciseDraft(name, exercise.ProgramExerciseId, exercise.Notes, exercise.Sets));
        }

        var updated = session.Update(request.PerformedOn, request.Title, request.Notes, request.DurationMin, drafts, timeProvider.GetUtcNow().UtcDateTime);

        if (!updated.IsSuccess)
        {
            return updated.Error!;
        }

        await db.SaveChangesAsync(cancellationToken);

        return new SavedSessionResponse(session.Id, await records.ForSessionAsync(session, cancellationToken));
    }

    public async Task<Result> Delete(Guid id, CurrentUser user, CancellationToken cancellationToken)
    {
        var session = await LoadAsync(id, user, cancellationToken);

        if (session is null)
        {
            return Error.NotFound("Session not found.");
        }

        db.Sessions.Remove(session);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private Task<WorkoutSession?> LoadAsync(Guid id, CurrentUser user, CancellationToken cancellationToken) =>
        db.Sessions
            .Include(s => s.Exercises)
            .SingleOrDefaultAsync(s => s.Id == id && s.UserId == user.Id, cancellationToken);
}

internal static class EditSessionEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPut("/sessions/{id:guid}", async (Guid id, SessionRequest request, ClaimsPrincipal user, EditSessionHandler handler, CancellationToken ct) =>
                (await handler.Update(id, request, CurrentUser.Required(user), ct)).ToHttp())
            .RequireAuthorization()
            .WithValidation<SessionRequest>()
            .WithName("UpdateSession");

        app.MapDelete("/sessions/{id:guid}", async (Guid id, ClaimsPrincipal user, EditSessionHandler handler, CancellationToken ct) =>
                (await handler.Delete(id, CurrentUser.Required(user), ct)).ToHttp())
            .RequireAuthorization()
            .WithName("DeleteSession");
    }
}
