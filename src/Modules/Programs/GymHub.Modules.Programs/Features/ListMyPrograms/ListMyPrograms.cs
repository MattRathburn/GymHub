using System.Security.Claims;
using GymHub.BuildingBlocks.Auth;
using GymHub.Modules.Programs.Contracts;
using GymHub.Modules.Programs.Data;
using GymHub.Modules.Programs.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Programs.Features.ListMyPrograms;

public sealed record MyProgramRow(Guid Id, string Title, ProgramKind Kind, ProgramStatus Status, int WeekCount, ProgramPrice Price, DateTime CreatedAt, DateTime? PublishedAt);

internal sealed class ListMyProgramsHandler(ProgramsDbContext db)
{
    public async Task<IReadOnlyList<MyProgramRow>> Handle(CurrentUser user, CancellationToken cancellationToken)
    {
        var programs = await db.Programs
            .AsNoTracking()
            .Include(p => p.Prices)
            .Include(p => p.Weeks)
            .Where(p => p.TrainerId == user.Id)
            .OrderByDescending(p => p.CreatedAt)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        return [.. programs.Select(p => new MyProgramRow(p.Id, p.Title, p.Kind, p.Status, p.Weeks.Count, p.CurrentPrice.Price, p.CreatedAt, p.PublishedAt))];
    }
}

internal static class ListMyProgramsEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/mine", async (ClaimsPrincipal user, ListMyProgramsHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.Handle(CurrentUser.Required(user), ct)))
            .RequireAuthorization(Policies.Trainer)
            .WithName("ListMyPrograms");
}
