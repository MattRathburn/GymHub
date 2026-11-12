using EventBus.Events;

namespace GymHub.Modules.Billing.Contracts;

/// <summary>Whether a trainer can sell paid programs: their Stripe Express account can take charges.</summary>
public interface IPayoutReadiness
{
    Task<bool> IsReadyAsync(string trainerId, CancellationToken cancellationToken);
}

public enum PaidAccessEndReason
{
    Cancelled,
    Refunded,
    Disputed,
    Unpaid
}

/// <summary>A purchase was paid for (or its trial started), so the buyer gets the program.</summary>
public sealed record PaidAccessGranted(string UserId, Guid ProgramId, Guid PurchaseId) : IntegrationEvent;

/// <summary>A purchase no longer gives access. Only ends access that came from this purchase.</summary>
public sealed record PaidAccessEnded(string UserId, Guid ProgramId, Guid PurchaseId, PaidAccessEndReason Reason) : IntegrationEvent;

/// <summary>The program became free, so the buyer keeps access as a free member.</summary>
public sealed record PaidAccessConvertedToFree(string UserId, Guid ProgramId, Guid PurchaseId) : IntegrationEvent;

/// <summary>A program was bought or subscribed to (trials included). For the trainer's "new subscriber" news.</summary>
public sealed record ProgramSold(string TrainerId, Guid ProgramId, string UserId, Guid PurchaseId, bool IsSubscription) : IntegrationEvent;

/// <summary>A subscriber moves to a new price at their next renewal. Interval is "Monthly" or "Yearly".</summary>
public sealed record SubscriptionPriceChangeScheduled(
    string UserId, Guid ProgramId, Guid PurchaseId, int PriceVersion, long AmountMinor, string Currency, string Interval, DateTime? EffectiveAt)
    : IntegrationEvent;

/// <summary>A subscription's free trial ends soon (Stripe warns 3 days ahead).</summary>
public sealed record SubscriptionTrialEnding(string UserId, Guid ProgramId, Guid PurchaseId, DateTime? TrialEndsAt) : IntegrationEvent;

/// <summary>A renewal payment failed; Stripe is retrying and access continues for now.</summary>
public sealed record SubscriptionPaymentFailed(string UserId, Guid ProgramId, Guid PurchaseId) : IntegrationEvent;
