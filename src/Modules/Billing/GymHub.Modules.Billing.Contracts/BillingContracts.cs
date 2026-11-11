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
