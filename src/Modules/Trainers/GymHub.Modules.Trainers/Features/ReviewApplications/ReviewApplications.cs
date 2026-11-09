using System.Security.Claims;
using FluentValidation;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Paging;
using GymHub.BuildingBlocks.Results;
using GymHub.BuildingBlocks.Validation;
using GymHub.Modules.Trainers.Data;
using GymHub.Modules.Trainers.Domain;
using GymHub.Modules.Trainers.Features.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Trainers.Features.ReviewApplications;

public sealed record RejectApplicationRequest(string Reason);

internal sealed class RejectApplicationValidator : AbstractValidator<RejectApplicationRequest>
{
    public RejectApplicationValidator() => RuleFor(r => r.Reason).NotEmpty().MaximumLength(500);
}

/// <summary>
/// Admins work through applications. Approving creates the public profile; the Keycloak role follows from the outbox.
/// </summary>
internal sealed class ReviewApplicationsHandler(TrainersDbContext db, TimeProvider timeProvider)
{
    public async Task<PagedResult<ApplicationDto>> List(ApplicationStatus status, PaginationRequest page, CancellationToken cancellationToken)
    {
        var applications = db.Applications.AsNoTracking().Where(a => a.Status == status);
        var count = await applications.LongCountAsync(cancellationToken);

        // Oldest pending first, so nobody waits forever; reviewed ones newest first.
        var ordered = status == ApplicationStatus.Pending
            ? applications.OrderBy(a => a.SubmittedAt)
            : applications.OrderByDescending(a => a.ReviewedAt);

        var items = await ordered.Skip(page.Skip).Take(page.Take).ToListAsync(cancellationToken);

        return new PagedResult<ApplicationDto>(page.PageIndex, page.Take, count, [.. items.Select(ApplicationDto.From)]);
    }

    public async Task<Result> Approve(Guid applicationId, CurrentUser admin, CancellationToken cancellationToken)
    {
        var application = await db.Applications.SingleOrDefaultAsync(a => a.Id == applicationId, cancellationToken);

        if (application is null)
        {
            return Error.NotFound("Application not found.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var approved = application.Approve(admin.Id, now);

        if (!approved.IsSuccess)
        {
            return approved;
        }

        var profile = await db.Profiles.SingleOrDefaultAsync(p => p.UserId == application.UserId, cancellationToken);

        if (profile is null)
        {
            db.Profiles.Add(TrainerProfile.Create(application.UserId, application.Details, now));
        }
        else
        {
            profile.Update(application.Details, now);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> Reject(Guid applicationId, RejectApplicationRequest request, CurrentUser admin, CancellationToken cancellationToken)
    {
        var application = await db.Applications.SingleOrDefaultAsync(a => a.Id == applicationId, cancellationToken);

        if (application is null)
        {
            return Error.NotFound("Application not found.");
        }

        var rejected = application.Reject(admin.Id, request.Reason, timeProvider.GetUtcNow().UtcDateTime);

        if (!rejected.IsSuccess)
        {
            return rejected;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal static class ReviewApplicationsEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var applications = app.MapGroup("/applications").RequireAuthorization(Policies.Admin);

        applications.MapGet("/", async ([AsParameters] PaginationRequest page, ReviewApplicationsHandler handler, CancellationToken ct, ApplicationStatus status = ApplicationStatus.Pending) =>
                TypedResults.Ok(await handler.List(status, page, ct)))
            .WithName("ListTrainerApplications");

        applications.MapPost("/{applicationId:guid}/approve", async (Guid applicationId, ClaimsPrincipal user, ReviewApplicationsHandler handler, CancellationToken ct) =>
                (await handler.Approve(applicationId, CurrentUser.Required(user), ct)).ToHttp())
            .WithName("ApproveTrainerApplication");

        applications.MapPost("/{applicationId:guid}/reject", async (Guid applicationId, RejectApplicationRequest request, ClaimsPrincipal user, ReviewApplicationsHandler handler, CancellationToken ct) =>
                (await handler.Reject(applicationId, request, CurrentUser.Required(user), ct)).ToHttp())
            .WithValidation<RejectApplicationRequest>()
            .WithName("RejectTrainerApplication");
    }
}
