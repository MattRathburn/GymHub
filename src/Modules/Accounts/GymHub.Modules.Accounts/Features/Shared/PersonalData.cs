using GymHub.BuildingBlocks.Privacy;

namespace GymHub.Modules.Accounts.Features.Shared;

/// <summary>Every module's <see cref="IPersonalDataSource"/>, asked together.</summary>
internal sealed class PersonalData(IEnumerable<IPersonalDataSource> sources)
{
    public IEnumerable<IPersonalDataSource> Sources => sources;

    /// <summary>The reasons the account can't be deleted yet; empty when it can.</summary>
    public async Task<IReadOnlyList<string>> GetBlockersAsync(string userId, CancellationToken cancellationToken)
    {
        var blockers = new List<string>();

        foreach (var source in sources)
        {
            if (await source.GetDeletionBlockerAsync(userId, cancellationToken) is { } blocker)
            {
                blockers.Add(blocker);
            }
        }

        return blockers;
    }
}
