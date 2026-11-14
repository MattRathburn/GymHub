using System.Security.Claims;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Paging;
using GymHub.BuildingBlocks.Results;
using GymHub.Modules.TrainingLog.Data;
using GymHub.Modules.TrainingLog.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.TrainingLog.Features.BrowseSessions;

public sealed record SessionsQuery(Guid? ProgramId = null, int PageIndex = 0, int PageSize = 20);

public sealed record SessionSummaryDto(
    Guid Id,
    DateOnly PerformedOn,
    string Title,
    Guid? ProgramId,
    string? ProgramTitle,
    int? WeekNumber,
    int? DurationMin,
    int ExerciseCount,
    int SetCount,
    decimal VolumeKg,
    IReadOnlyList<string> ExerciseNames);

public sealed record LoggedExerciseDto(
    Guid Id,
    int Order,
    string Name,
    string ExerciseKey,
    Guid? ProgramExerciseId,
    string? Notes,
    IReadOnlyList<LoggedSet> Sets,
    decimal? TopWeightKg,
    decimal? BestE1RmKg,
    decimal VolumeKg);

public sealed record SessionDto(
    Guid Id,
    DateOnly PerformedOn,
    string Title,
    string? Notes,
    int? DurationMin,
    Guid? ProgramId,
    string? ProgramTitle,
    int? WeekNumber,
    Guid? ProgramWorkoutId,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<LoggedExerciseDto> Exercises);

/// <summary>The member's own sessions, newest first.</summary>
internal sealed class BrowseSessionsHandler(TrainingLogDbContext db)
{
    private const int NamesInSummary = 4;

    public async Task<PagedResult<SessionSummaryDto>> List(CurrentUser user, SessionsQuery query, CancellationToken cancellationToken)
    {
        var paging = new PaginationRequest(query.PageSize, query.PageIndex);
        var sessions = db.Sessions.AsNoTracking().Where(s => s.UserId == user.Id);

        if (query.ProgramId is { } programId)
        {
            sessions = sessions.Where(s => s.ProgramId == programId);
        }

        var count = await sessions.LongCountAsync(cancellationToken);
        var page = await sessions
            .OrderByDescending(s => s.PerformedOn)
            .ThenByDescending(s => s.CreatedAt)
            .Skip(paging.Skip)
            .Take(paging.Take)
            .Select(s => new SessionSummaryDto(
                s.Id,
                s.PerformedOn,
                s.Title,
                s.ProgramId,
                s.ProgramTitle,
                s.WeekNumber,
                s.DurationMin,
                s.Exercises.Count,
                s.Exercises.Sum(e => e.SetCount),
                s.Exercises.Sum(e => e.VolumeKg),
                s.Exercises.OrderBy(e => e.Order).Select(e => e.Name).Take(NamesInSummary).ToList()))
            .ToListAsync(cancellationToken);

        return new PagedResult<SessionSummaryDto>(paging.PageIndex, paging.Take, count, page);
    }

    public async Task<Result<SessionDto>> Get(Guid id, CurrentUser user, CancellationToken cancellationToken)
    {
        var session = await db.Sessions
            .AsNoTracking()
            .Include(s => s.Exercises)
            .SingleOrDefaultAsync(s => s.Id == id && s.UserId == user.Id, cancellationToken);

        if (session is null)
        {
            return Error.NotFound("Session not found.");
        }

        return new SessionDto(
            session.Id,
            session.PerformedOn,
            session.Title,
            session.Notes,
            session.DurationMin,
            session.ProgramId,
            session.ProgramTitle,
            session.WeekNumber,
            session.ProgramWorkoutId,
            session.CreatedAt,
            session.UpdatedAt,
            [.. session.Exercises.OrderBy(e => e.Order).Select(e => new LoggedExerciseDto(
                e.Id, e.Order, e.Name, e.ExerciseKey, e.ProgramExerciseId, e.Notes, e.Sets, e.TopWeightKg, e.BestE1RmKg, e.VolumeKg))]);
    }
}

internal static class BrowseSessionsEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/sessions", async ([AsParameters] SessionsQuery query, ClaimsPrincipal user, BrowseSessionsHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.List(CurrentUser.Required(user), query, ct)))
            .RequireAuthorization()
            .WithName("ListSessions");

        app.MapGet("/sessions/{id:guid}", async (Guid id, ClaimsPrincipal user, BrowseSessionsHandler handler, CancellationToken ct) =>
                (await handler.Get(id, CurrentUser.Required(user), ct)).ToHttp())
            .RequireAuthorization()
            .WithName("GetSession");
    }
}
