namespace GymHub.Modules.Discussions.Features.Shared;

/// <summary>
/// Tells open boards and threads that something changed, after it is saved. Messages carry ids only: clients refetch
/// through the REST endpoints, which apply each viewer's access (so hidden content never leaks to non-moderators).
/// Best effort; a missed message only means a stale view until the next refresh.
/// </summary>
internal interface IBoardNotifier
{
    Task ThreadChangedAsync(Guid programId, Guid threadId);
}

internal static class BoardGroups
{
    public static string Board(Guid programId) => $"board:{programId}";

    public static string Thread(Guid threadId) => $"thread:{threadId}";
}
