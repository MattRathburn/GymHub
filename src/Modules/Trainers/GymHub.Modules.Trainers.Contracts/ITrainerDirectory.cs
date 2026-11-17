namespace GymHub.Modules.Trainers.Contracts;

/// <summary>Trainer lookups for catalog search.</summary>
public interface ITrainerDirectory
{
    /// <summary>Every trainer whose profile name contains the text (case-insensitive).</summary>
    Task<IReadOnlyList<string>> FindTrainerIdsAsync(string nameContains, CancellationToken cancellationToken);
}
