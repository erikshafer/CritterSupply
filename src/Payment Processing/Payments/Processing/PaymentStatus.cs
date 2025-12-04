namespace Payments.Processing;

/// <summary>
/// Represents the current status of a payment.
/// </summary>
public enum PaymentStatus
{
    Pending,
    Captured,
    Failed,
    Refunded
}
