namespace GymHub.Modules.Subscriptions.Contracts;

/// <summary>Answers whether a user may see a program's members-only content.</summary>
public interface IEntitlements
{
    Task<bool> HasAccessAsync(string userId, Guid programId, CancellationToken cancellationToken);
}
