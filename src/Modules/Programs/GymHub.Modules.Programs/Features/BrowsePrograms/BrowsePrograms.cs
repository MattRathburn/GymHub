using GymHub.BuildingBlocks.Paging;
using GymHub.Modules.Programs.Contracts;
using GymHub.Modules.Programs.Data;
using GymHub.Modules.Programs.Domain;
using GymHub.Modules.Programs.Features.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Programs.Features.BrowsePrograms;

public sealed record BrowseProgramsQuery(string? Search, string? TrainerId, ProgramKind? Kind, bool? Free);

public sealed record ProgramCard(Guid Id, string Title, string? Summary, string TrainerId, ProgramKind Kind, DateOnly? StartsOn, DateOnly? EndsOn, ProgramPrice Price, Uri? CoverUrl, int WeekCount);

internal sealed class BrowseProgramsHandler(ProgramsDbContext db, ProgramContentMapper mapper)
{
    public async Task<PagedResult<ProgramCard>> Handle(BrowseProgramsQuery query, PaginationRequest page, CancellationToken cancellationToken)
    {
        var programs = db.Programs.AsNoTracking().Where(p => p.Status == ProgramStatus.Published);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{query.Search.Trim()}%";
            programs = programs.Where(p => EF.Functions.ILike(p.Title, pattern));
        }

        if (!string.IsNullOrWhiteSpace(query.TrainerId))
        {
            programs = programs.Where(p => p.TrainerId == query.TrainerId);
        }

        if (query.Kind is { } kind)
        {
            programs = programs.Where(p => p.Kind == kind);
        }

        if (query.Free is { } free)
        {
            programs = programs.Where(p => p.IsFree == free);
        }

        var count = await programs.LongCountAsync(cancellationToken);

        var items = await programs
            .Include(p => p.Prices)
            .Include(p => p.Weeks)
            .OrderByDescending(p => p.PublishedAt)
            .Skip(page.Skip)
            .Take(page.Take)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        var cards = items.Select(p => new ProgramCard(
            p.Id, p.Title, p.Summary, p.TrainerId, p.Kind, p.StartsOn, p.EndsOn,
            p.CurrentPrice.Price, mapper.CoverUrl(p), p.Weeks.Count)).ToList();

        return new PagedResult<ProgramCard>(page.PageIndex, page.Take, count, cards);
    }
}

internal static class BrowseProgramsEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/", async ([AsParameters] BrowseProgramsQuery query, [AsParameters] PaginationRequest page, BrowseProgramsHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.Handle(query, page, ct)))
            .AllowAnonymous()
            .WithName("BrowsePrograms");
}
