using EventBus.Events;
using GymHub.BuildingBlocks.Results;
using GymHub.Modules.Trainers.Contracts;

namespace GymHub.Modules.Trainers.Domain;

public enum ApplicationStatus
{
    Pending,
    Approved,
    Rejected
}

/// <summary>
/// A user's request to become a trainer. An admin approves it (creating the profile and granting the Keycloak role)
/// or rejects it. A user has at most one pending application and may reapply after a rejection.
/// </summary>
public sealed class TrainerApplication
{
    private readonly List<IntegrationEvent> _events = [];

    private TrainerApplication() { }

    public Guid Id { get; private set; }
    public string UserId { get; private set; } = "";
    public string DisplayName { get; private set; } = "";
    public string? Bio { get; private set; }
    public List<string> Specialties { get; private set; } = [];
    public List<string> Links { get; private set; } = [];
    public ApplicationStatus Status { get; private set; }
    public DateTime SubmittedAt { get; private set; }
    public string? ReviewedBy { get; private set; }
    public DateTime? ReviewedAt { get; private set; }
    public string? RejectionReason { get; private set; }

    internal IReadOnlyList<IntegrationEvent> Events => _events;

    internal void ClearEvents() => _events.Clear();

    public TrainerDetails Details => new(DisplayName, Bio, Specialties, Links);

    /// <param name="isAlreadyTrainer">The caller already holds the trainer role.</param>
    /// <param name="previous">The user's earlier applications.</param>
    public static Result<TrainerApplication> Submit(
        string userId, TrainerDetails details, bool isAlreadyTrainer, IEnumerable<TrainerApplication> previous, DateTime now)
    {
        var earlier = previous.ToList();

        if (isAlreadyTrainer || earlier.Any(a => a.Status == ApplicationStatus.Approved))
        {
            return Error.Conflict("You're already a trainer.");
        }

        if (earlier.Any(a => a.Status == ApplicationStatus.Pending))
        {
            return Error.Conflict("You already have an application waiting for review.");
        }

        return new TrainerApplication
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            DisplayName = details.DisplayName,
            Bio = details.Bio,
            Specialties = [.. details.Specialties],
            Links = [.. details.Links],
            Status = ApplicationStatus.Pending,
            SubmittedAt = now
        };
    }

    /// <summary>Approves the application and raises <see cref="TrainerApproved"/>, which grants the Keycloak role.</summary>
    public Result Approve(string adminId, DateTime now)
    {
        if (Status != ApplicationStatus.Pending)
        {
            return AlreadyReviewed();
        }

        Status = ApplicationStatus.Approved;
        ReviewedBy = adminId;
        ReviewedAt = now;
        _events.Add(new TrainerApproved(UserId, Id, DisplayName));
        return Result.Success();
    }

    public Result Reject(string adminId, string reason, DateTime now)
    {
        if (Status != ApplicationStatus.Pending)
        {
            return AlreadyReviewed();
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Invalid("A reason is required to reject an application.");
        }

        Status = ApplicationStatus.Rejected;
        ReviewedBy = adminId;
        ReviewedAt = now;
        RejectionReason = reason.Trim();
        return Result.Success();
    }

    private Error AlreadyReviewed() => Error.Conflict($"This application was already {Status.ToString().ToLowerInvariant()}.");
}
