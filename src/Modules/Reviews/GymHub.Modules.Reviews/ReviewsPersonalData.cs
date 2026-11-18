using GymHub.BuildingBlocks.Privacy;
using GymHub.Modules.Reviews.Data;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Reviews;

/// <summary>Reviews stay word for word and keep counting toward the rating; their author becomes "Deleted user".</summary>
internal sealed class ReviewsPersonalData(ReviewsDbContext db) : IPersonalDataSource
{
    public string Section => "reviews";

    public async Task<object> ExportAsync(string userId, CancellationToken cancellationToken)
    {
        var reviews = await db.Reviews.AsNoTracking()
            .Where(r => r.AuthorId == userId)
            .OrderBy(r => r.CreatedAt)
            .Select(r => new { r.Id, r.ProgramId, r.Stars, r.Body, r.CreatedAt, r.UpdatedAt, r.IsHidden, r.Reply })
            .ToListAsync(cancellationToken);

        var reports = await db.Reports.AsNoTracking()
            .Where(r => r.ReporterId == userId)
            .OrderBy(r => r.CreatedAt)
            .Select(r => new { r.ReviewId, r.Reason, r.Note, r.Status, r.CreatedAt })
            .ToListAsync(cancellationToken);

        return new { reviews, reports };
    }

    public async Task EraseAsync(string userId, CancellationToken cancellationToken)
    {
        await db.Reviews.Where(r => r.AuthorId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.AuthorName, DeletedUser.DisplayName), cancellationToken);
        await db.Reports.Where(r => r.ReporterId == userId).ExecuteDeleteAsync(cancellationToken);
    }
}
