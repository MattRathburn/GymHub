using System.Security.Claims;
using GymHub.BuildingBlocks.Auth;
using GymHub.Modules.Programs.Contracts;
using GymHub.Modules.Subscriptions.Data;
using GymHub.Modules.Subscriptions.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Subscriptions.Features.ListMyEnrollments;

public sealed record MyEnrollment(Guid ProgramId, string? ProgramTitle, ProgramStatus? ProgramStatus, EnrollmentSource Source, DateTime EnrolledAt);

internal sealed class ListMyEnrollmentsHandler(SubscriptionsDbContext db, IProgramCatalog catalog)
{
    public async Task<IReadOnlyList<MyEnrollment>> Handle(CurrentUser user, CancellationToken cancellationToken)
    {
        var enrollments = await db.Enrollments
            .AsNoTracking()
            .Where(e => e.UserId == user.Id && e.Status == EnrollmentStatus.Active)
            .OrderByDescending(e => e.EnrolledAt)
            .ToListAsync(cancellationToken);

        var programs = await catalog.GetSummariesAsync([.. enrollments.Select(e => e.ProgramId)], cancellationToken);

        return [.. enrollments.Select(e =>
        {
            var program = programs.GetValueOrDefault(e.ProgramId);
            return new MyEnrollment(e.ProgramId, program?.Title, program?.Status, e.Source, e.EnrolledAt);
        })];
    }
}

internal static class ListMyEnrollmentsEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/mine", async (ClaimsPrincipal user, ListMyEnrollmentsHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.Handle(CurrentUser.Required(user), ct)))
            .RequireAuthorization()
            .WithName("ListMyEnrollments");
}
