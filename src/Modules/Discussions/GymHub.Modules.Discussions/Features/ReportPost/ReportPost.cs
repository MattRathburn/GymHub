using System.Security.Claims;
using FluentValidation;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Results;
using GymHub.BuildingBlocks.Validation;
using GymHub.Modules.Discussions.Data;
using GymHub.Modules.Discussions.Domain;
using GymHub.Modules.Discussions.Features.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Discussions.Features.ReportPost;

public sealed record ReportPostRequest(ReportReason Reason, string? Note);

internal sealed class ReportPostValidator : AbstractValidator<ReportPostRequest>
{
    public ReportPostValidator()
    {
        RuleFor(r => r.Reason).IsInEnum();
        RuleFor(r => r.Note).MaximumLength(PostReport.MaxNoteLength);
    }
}

/// <summary>Anyone who can see the board flags a post for admins. Reporting the same post again is a no-op.</summary>
internal sealed class ReportPostHandler(DiscussionsDbContext db, BoardAccess access, TimeProvider timeProvider)
{
    public async Task<Result> Handle(Guid postId, ReportPostRequest request, CurrentUser user, CancellationToken cancellationToken)
    {
        var opened = await access.OpenPostAsync(db, postId, user, cancellationToken);

        if (!opened.IsSuccess)
        {
            return opened.Error!;
        }

        var (post, thread, _) = opened.Value;

        if (post.IsDeleted)
        {
            return Error.Conflict("This post was deleted.");
        }

        if (await db.Reports.AnyAsync(r => r.PostId == postId && r.ReporterId == user.Id && r.Status == ReportStatus.Open, cancellationToken))
        {
            return Result.Success();
        }

        var report = PostReport.File(post, thread.ProgramId, user.Id, request.Reason, request.Note, timeProvider.GetUtcNow().UtcDateTime);

        if (!report.IsSuccess)
        {
            return report.Error!;
        }

        db.Reports.Add(report.Value!);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A concurrent duplicate submit may have won the unique index; then the report exists either way.
            if (!await db.Reports.AsNoTracking().AnyAsync(
                    r => r.PostId == postId && r.ReporterId == user.Id && r.Status == ReportStatus.Open, cancellationToken))
            {
                throw;
            }
        }

        return Result.Success();
    }
}

internal static class ReportPostEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/posts/{postId:guid}/reports", async (
                    Guid postId, ReportPostRequest request, ClaimsPrincipal user, ReportPostHandler handler, CancellationToken ct) =>
                (await handler.Handle(postId, request, CurrentUser.Required(user), ct)).ToHttp())
            .RequireAuthorization()
            .WithValidation<ReportPostRequest>()
            .WithName("ReportPost");
}
