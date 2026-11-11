using EventBus.Events;

namespace GymHub.Modules.Programs.Contracts;

public enum ProgramStatus
{
    Draft,
    Published,
    Archived
}

public enum PricingModel
{
    Free,
    OneTime,
    Recurring
}

public enum BillingInterval
{
    Monthly,
    Yearly
}

/// <summary>
/// A program's price. Amounts are in minor units (cents) of an ISO 4217 currency. TrialDays is a free trial for
/// recurring prices only.
/// </summary>
public sealed record ProgramPrice(PricingModel Model, long? AmountMinor, string? Currency, BillingInterval? Interval, int? TrialDays = null)
{
    public const int MaxTrialDays = 30;

    public static ProgramPrice Free { get; } = new(PricingModel.Free, null, null, null);

    public bool IsFree => Model == PricingModel.Free;
}

public sealed record ProgramSummary(Guid Id, string TrainerId, string Title, ProgramStatus Status, ProgramPrice Price, int PriceVersion);

/// <summary>Synchronous reads of the program catalog for other modules.</summary>
public interface IProgramCatalog
{
    Task<ProgramSummary?> GetSummaryAsync(Guid programId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, ProgramSummary>> GetSummariesAsync(IReadOnlyCollection<Guid> programIds, CancellationToken cancellationToken);
}

public sealed record ProgramPublished(Guid ProgramId, string TrainerId, string Title, ProgramPrice Price, int PriceVersion) : IntegrationEvent;

public sealed record ProgramPricingChanged(Guid ProgramId, ProgramPrice Price, int PriceVersion) : IntegrationEvent;

public sealed record ProgramArchived(Guid ProgramId) : IntegrationEvent;

public sealed record ProgramWeekReleased(Guid ProgramId, Guid WeekId, int WeekNumber, string WeekTitle, DateOnly ReleasesOn) : IntegrationEvent;
