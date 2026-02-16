namespace Payments.Processing;

/// <summary>
/// Response DTO for payment queries.
/// </summary>
public sealed record PaymentResponse(
    Guid PaymentId,
    Guid OrderId,
    decimal Amount,
    string Currency,
    PaymentStatus Status,
    string? TransactionId,
    string? FailureReason,
    DateTimeOffset InitiatedAt,
    DateTimeOffset? ProcessedAt)
{
    /// <summary>
    /// Creates a PaymentResponse from a Payment aggregate.
    /// </summary>
    public static PaymentResponse From(Payment payment) => new(
        payment.Id,
        payment.OrderId,
        payment.Amount,
        payment.Currency,
        payment.Status,
        payment.TransactionId,
        payment.FailureReason,
        payment.InitiatedAt,
        payment.ProcessedAt);
}
