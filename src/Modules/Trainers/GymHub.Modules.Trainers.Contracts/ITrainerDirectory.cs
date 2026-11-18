namespace GymHub.Modules.Trainers.Contracts;

/// <summary>Trainer lookups for catalog search.</summary>
public interface ITrainerDirectory
{
    /// <summary>Every trainer whose profile name contains the text (case-insensitive).</summary>
    Task<IReadOnlyList<string>> FindTrainerIdsAsync(string nameContains, CancellationToken cancellationToken);

    /// <summary>
    /// True when an admin revoked the user's trainer role (and no new approval followed). Checked when publishing,
    /// because the user's current token can still carry the role for a few minutes.
    /// </summary>
    Task<bool> IsRevokedAsync(string userId, CancellationToken cancellationToken);
}
