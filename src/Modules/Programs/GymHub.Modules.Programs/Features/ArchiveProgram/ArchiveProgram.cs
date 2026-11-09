using System.Security.Claims;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Results;
using GymHub.Modules.Programs.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace GymHub.Modules.Programs.Features.ArchiveProgram;

/// <summary>Archived programs leave the catalog. The trainer archives their own; admins can archive any (moderation).</summary>
internal sealed class ArchiveProgramHandler(ProgramsDbContext db)
{
    public async Task<Result> Handle(Guid programId, CurrentUser user, CancellationToken cancellationToken)
    {
        var program = await db.LoadProgramAsync(programId, cancellationToken);

        if (program is null)
        {
            return Error.NotFound("Program not found.");
        }

        if (!program.IsOwnedBy(user.Id) && !user.IsAdmin)
        {
            return Error.Forbidden("Only the program's trainer or an admin can archive it.");
        }

        var result = program.Archive();

        if (result.IsSuccess)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}

internal static class ArchiveProgramEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/{programId:guid}/archive", async (Guid programId, ClaimsPrincipal user, ArchiveProgramHandler handler, CancellationToken ct) =>
                (await handler.Handle(programId, CurrentUser.Required(user), ct)).ToHttp())
            .RequireAuthorization(Policies.Trainer)
            .WithName("ArchiveProgram");
}
