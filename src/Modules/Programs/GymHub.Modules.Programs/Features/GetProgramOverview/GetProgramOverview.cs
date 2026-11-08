using GymHub.BuildingBlocks.Results;
using GymHub.Modules.Programs.Contracts;
using GymHub.Modules.Programs.Data;
using GymHub.Modules.Programs.Domain;
using GymHub.Modules.Programs.Features.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace GymHub.Modules.Programs.Features.GetProgramOverview;

public sealed record WeekOutline(Guid Id, int Number, string Title, DateOnly ReleasesOn, bool IsReleased);

/// <summary>The public sales page: no workout content.</summary>
public sealed record ProgramOverview(
    Guid Id, string TrainerId, string Title, string? Summary, string? Description,
    ProgramKind Kind, DateOnly? StartsOn, DateOnly? EndsOn, ProgramPrice Price,
    Uri? CoverUrl, IReadOnlyList<MediaDto> Media, IReadOnlyList<WeekOutline> Weeks);

internal sealed class GetProgramOverviewHandler(ProgramsDbContext db, ProgramContentMapper mapper)
{
    public async Task<Result<ProgramOverview>> Handle(Guid programId, CancellationToken cancellationToken)
    {
        var program = await db.LoadProgramAsync(programId, cancellationToken);

        if (program is null || program.Status != ProgramStatus.Published)
        {
            return Error.NotFound("Program not found.");
        }

        var today = mapper.Today;

        return new ProgramOverview(
            program.Id, program.TrainerId, program.Title, program.Summary, program.Description,
            program.Kind, program.StartsOn, program.EndsOn, program.CurrentPrice.Price,
            mapper.CoverUrl(program), mapper.MediaFor(program, MediaTarget.Program, program.Id),
            [.. program.Weeks.OrderBy(w => w.Number).Select(w => new WeekOutline(w.Id, w.Number, w.Title, w.ReleasesOn, w.IsReleased(today)))]);
    }
}

internal static class GetProgramOverviewEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/{programId:guid}", async (Guid programId, GetProgramOverviewHandler handler, CancellationToken ct) =>
                (await handler.Handle(programId, ct)).ToHttp())
            .AllowAnonymous()
            .WithName("GetProgramOverview");
}
