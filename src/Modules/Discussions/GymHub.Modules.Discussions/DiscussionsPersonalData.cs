using GymHub.BuildingBlocks.Privacy;
using GymHub.Modules.Discussions.Data;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Discussions;

/// <summary>Posts stay word for word so threads keep making sense; their author becomes "Deleted user".</summary>
internal sealed class DiscussionsPersonalData(DiscussionsDbContext db) : IPersonalDataSource
{
    public string Section => "discussions";

    public async Task<object> ExportAsync(string userId, CancellationToken cancellationToken)
    {
        var threads = await db.Threads.AsNoTracking()
            .Where(t => t.AuthorId == userId)
            .OrderBy(t => t.CreatedAt)
            .Select(t => new { t.Id, t.ProgramId, t.Kind, t.Title, t.CreatedAt })
            .ToListAsync(cancellationToken);

        var posts = await db.Posts.AsNoTracking()
            .Where(p => p.AuthorId == userId && p.DeletedAt == null)
            .OrderBy(p => p.CreatedAt)
            .Select(p => new { p.Id, p.ThreadId, p.ParentPostId, p.Body, p.CreatedAt, p.EditedAt })
            .ToListAsync(cancellationToken);

        var reports = await db.Reports.AsNoTracking()
            .Where(r => r.ReporterId == userId)
            .OrderBy(r => r.CreatedAt)
            .Select(r => new { r.PostId, r.Reason, r.Note, r.Status, r.CreatedAt })
            .ToListAsync(cancellationToken);

        return new { threads, posts, reports };
    }

    public async Task EraseAsync(string userId, CancellationToken cancellationToken)
    {
        await db.Posts.Where(p => p.AuthorId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.AuthorName, DeletedUser.DisplayName), cancellationToken);
        await db.Reports.Where(r => r.ReporterId == userId).ExecuteDeleteAsync(cancellationToken);
    }
}
