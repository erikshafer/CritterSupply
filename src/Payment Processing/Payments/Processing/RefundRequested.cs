namespace Payments.Processing;

/// <summary>
/// Command from Orders context requesting a refund.
/// </summary>
public sealed record RefundRequested(
    Guid PaymentId,
    Guid OrderId,
    decimal Amount);
