using JasperFx.Events;

namespace Payments.Processing;

/// <summary>
/// Event-sourced aggregate representing a payment.
/// Write-only model - contains no behavior, only Apply() methods for event sourcing.
/// All business logic lives in command handlers following the Decider pattern.
/// </summary>
public sealed record Payment(
    Guid Id,
    Guid OrderId,
    Guid CustomerId,
    decimal Amount,
    string Currency,
    string PaymentMethodToken,
    PaymentStatus Status,
    string? TransactionId,
    string? AuthorizationId,
    DateTimeOffset? AuthorizationExpiresAt,
    string? FailureReason,
    bool IsRetriable,
    DateTimeOffset InitiatedAt,
    DateTimeOffset? ProcessedAt,
    decimal TotalRefunded = 0m)
{
    /// <summary>
    /// Gets the remaining amount available for refund.
    /// </summary>
    public decimal RefundableAmount => Amount - TotalRefunded;

    public static Payment Create(PaymentInitiated @event) =>
        new(@event.PaymentId,
            @event.OrderId,
            @event.CustomerId,
            @event.Amount,
            @event.Currency,
            @event.PaymentMethodToken,
            PaymentStatus.Pending,
            null,
            null,
            null,
            null,
            false,
            @event.InitiatedAt,
            null,
            0m);

    public Payment Apply(PaymentAuthorized @event) =>
        this with
        {
            Status = PaymentStatus.Authorized,
            AuthorizationId = @event.AuthorizationId,
            AuthorizationExpiresAt = @event.AuthorizedAt.AddDays(7),
            ProcessedAt = @event.AuthorizedAt
        };

    public Payment Apply(PaymentCaptured @event) =>
        this with
        {
            Status = PaymentStatus.Captured,
            TransactionId = @event.TransactionId,
            ProcessedAt = @event.CapturedAt
        };

    public Payment Apply(PaymentFailed @event) =>
        this with
        {
            Status = PaymentStatus.Failed,
            FailureReason = @event.FailureReason,
            IsRetriable = @event.IsRetriable,
            ProcessedAt = @event.FailedAt
        };

    public Payment Apply(PaymentRefunded @event) =>
        this with
        {
            Status = @event.TotalRefunded >= Amount ? PaymentStatus.Refunded : Status,
            TotalRefunded = @event.TotalRefunded
        };
}
