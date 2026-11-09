using GymHub.BuildingBlocks.Storage;
using GymHub.Modules.Trainers.Domain;

namespace GymHub.Modules.Trainers.Features.Shared;

public sealed record ApplicationDto(
    Guid Id, string UserId, string DisplayName, string? Bio, IReadOnlyList<string> Specialties, IReadOnlyList<string> Links,
    ApplicationStatus Status, DateTime SubmittedAt, DateTime? ReviewedAt, string? RejectionReason)
{
    public static ApplicationDto From(TrainerApplication a) => new(
        a.Id, a.UserId, a.DisplayName, a.Bio, a.Specialties, a.Links, a.Status, a.SubmittedAt, a.ReviewedAt, a.RejectionReason);
}

internal static class AvatarUrls
{
    private static readonly TimeSpan UrlLifetime = TimeSpan.FromHours(1);

    public static Uri? AvatarUrl(this IObjectStorage storage, string? objectKey) =>
        objectKey is null ? null : storage.CreateDownloadUrl(objectKey, UrlLifetime);
}
