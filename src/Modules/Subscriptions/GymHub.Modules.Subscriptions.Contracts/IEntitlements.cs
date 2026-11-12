namespace GymHub.Modules.Subscriptions.Contracts;

/// <summary>Answers whether a user may see a program's members-only content.</summary>
public interface IEntitlements
{
    Task<bool> HasAccessAsync(string userId, Guid programId, CancellationToken cancellationToken);
}

/// <summary>Who currently has a program, for messages to all its members.</summary>
public interface IProgramMembers
{
    Task<IReadOnlyList<string>> GetActiveMemberIdsAsync(Guid programId, CancellationToken cancellationToken);
}
