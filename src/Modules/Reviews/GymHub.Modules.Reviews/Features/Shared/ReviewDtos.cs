using System.Security.Claims;
using GymHub.Modules.Reviews.Domain;
using GymHub.Modules.Trainers.Contracts;

namespace GymHub.Modules.Reviews.Features.Shared;

public sealed record ReviewerDto(string UserId, string DisplayName, Uri? AvatarUrl);

public sealed record ReplyDto(string Body, string TrainerName, DateTime RepliedAt, DateTime UpdatedAt);

public sealed record ReviewDto(
    Guid Id, Guid ProgramId, ReviewerDto Author, int Stars, string? Body, DateTime CreatedAt, DateTime UpdatedAt,
    bool IsHidden, ReplyDto? Reply);

/// <summary>Turns reviews into DTOs with current profile names, falling back to the username stored on the review.</summary>
internal sealed class ReviewMapper(IProfileDirectory directory)
{
    public async Task<IReadOnlyList<ReviewDto>> MapAsync(IReadOnlyCollection<Review> reviews, CancellationToken cancellationToken)
    {
        var userIds = reviews.SelectMany(r => new[] { r.AuthorId, r.TrainerId }).Distinct().ToList();
        var profiles = userIds.Count == 0 ? new Dictionary<string, AuthorInfo>() : await directory.GetAuthorsAsync(userIds, cancellationToken);

        return [.. reviews.Select(r =>
        {
            var author = profiles.TryGetValue(r.AuthorId, out var profile)
                ? new ReviewerDto(r.AuthorId, profile.DisplayName, profile.AvatarUrl)
                : new ReviewerDto(r.AuthorId, r.AuthorName, null);

            var reply = r.Reply is null
                ? null
                : new ReplyDto(r.Reply, profiles.GetValueOrDefault(r.TrainerId)?.DisplayName ?? "The trainer", r.RepliedAt!.Value, r.ReplyUpdatedAt!.Value);

            return new ReviewDto(r.Id, r.ProgramId, author, r.Stars, r.Body, r.CreatedAt, r.UpdatedAt, r.IsHidden, reply);
        })];
    }

    public async Task<ReviewDto> MapAsync(Review review, CancellationToken cancellationToken) =>
        (await MapAsync([review], cancellationToken))[0];

    /// <summary>The caller's username, stored on their review in case they never set up a profile.</summary>
    public static string SnapshotName(ClaimsPrincipal user) =>
        user.FindFirst("preferred_username")?.Value ?? user.Identity?.Name ?? "Member";
}
