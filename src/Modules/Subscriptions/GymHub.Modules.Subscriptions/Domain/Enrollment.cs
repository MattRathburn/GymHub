namespace GymHub.Modules.Subscriptions.Domain;

public enum EnrollmentStatus
{
    Active,
    Cancelled
}

public enum EnrollmentSource
{
    Free,

    /// <summary>Paid through Stripe Checkout (Billing plan).</summary>
    Stripe
}

/// <summary>A user's access to one program. One row per user and program; cancelling and re-enrolling reuses it.</summary>
public sealed class Enrollment
{
    private Enrollment() { }

    public Guid Id { get; private set; }
    public string UserId { get; private set; } = "";
    public Guid ProgramId { get; private set; }
    public EnrollmentStatus Status { get; private set; }
    public EnrollmentSource Source { get; private set; }
    public DateTime EnrolledAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }

    /// <summary>The Billing purchase that gives access, for Stripe enrollments.</summary>
    public Guid? PurchaseId { get; private set; }

    public bool IsActive => Status == EnrollmentStatus.Active;

    public static Enrollment StartFree(string userId, Guid programId, DateTime now) => new()
    {
        Id = Guid.CreateVersion7(),
        UserId = userId,
        ProgramId = programId,
        Status = EnrollmentStatus.Active,
        Source = EnrollmentSource.Free,
        EnrolledAt = now
    };

    public static Enrollment StartPaid(string userId, Guid programId, Guid purchaseId, DateTime now) => new()
    {
        Id = Guid.CreateVersion7(),
        UserId = userId,
        ProgramId = programId,
        Status = EnrollmentStatus.Active,
        Source = EnrollmentSource.Stripe,
        PurchaseId = purchaseId,
        EnrolledAt = now
    };

    public void Reactivate(DateTime now)
    {
        Status = EnrollmentStatus.Active;
        Source = EnrollmentSource.Free;
        PurchaseId = null;
        EnrolledAt = now;
        CancelledAt = null;
    }

    /// <summary>Access now comes from this purchase, whatever the row held before.</summary>
    public void ActivatePaid(Guid purchaseId, DateTime now)
    {
        if (!IsActive)
        {
            EnrolledAt = now;
        }

        Status = EnrollmentStatus.Active;
        Source = EnrollmentSource.Stripe;
        PurchaseId = purchaseId;
        CancelledAt = null;
    }

    /// <summary>The program became free: keep access without the purchase.</summary>
    public void ConvertToFree()
    {
        Source = EnrollmentSource.Free;
        PurchaseId = null;
    }

    public void Cancel(DateTime now)
    {
        Status = EnrollmentStatus.Cancelled;
        CancelledAt = now;
    }
}
