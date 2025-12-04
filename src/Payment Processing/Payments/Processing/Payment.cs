namespace Payments.Processing;

/// <summary>
/// Event-sourced aggregate representing a payment.
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
    string? FailureReason,
    bool IsRetriable,
    DateTimeOffset InitiatedAt,
    DateTimeOffset? ProcessedAt)
{
    /// <summary>
    /// Collection of uncommitted events for this aggregate.
    /// </summary>
    internal List<object> PendingEvents { get; } = [];

    /// <summary>
    /// Creates a new Payment from a PaymentRequested command.
    /// Generates unique ID, sets status to Pending, and records timestamp.
    /// </summary>
    public static Payment Create(PaymentRequested command)
    {
        var paymentId = Guid.CreateVersion7();
        var initiatedAt = DateTimeOffset.UtcNow;

        var payment = new Payment(
            paymentId,
            command.OrderId,
            command.CustomerId,
            command.Amount,
            command.Currency,
            command.PaymentMethodToken,
            PaymentStatus.Pending,
            null,
            null,
            false,
            initiatedAt,
            null);

        payment.PendingEvents.Add(new PaymentInitiated(
            paymentId,
            command.OrderId,
            command.CustomerId,
            command.Amount,
            command.Currency,
            command.PaymentMethodToken,
            initiatedAt));

        return payment;
    }

    /// <summary>
    /// Captures the payment with the given transaction ID.
    /// Updates status to Captured and returns integration message for Orders context.
    /// </summary>
    public (Payment, PaymentCapturedIntegration) Capture(string transactionId, DateTimeOffset capturedAt)
    {
        var updated = this with
        {
            Status = PaymentStatus.Captured,
            TransactionId = transactionId,
            ProcessedAt = capturedAt
        };

        updated.PendingEvents.AddRange(PendingEvents);
        updated.PendingEvents.Add(new PaymentCaptured(Id, transactionId, capturedAt));

        var integrationMessage = new PaymentCapturedIntegration(Id, OrderId, Amount, transactionId, capturedAt);

        return (updated, integrationMessage);
    }

    /// <summary>
    /// Fails the payment with the given reason.
    /// Updates status to Failed and returns integration message for Orders context.
    /// </summary>
    public (Payment, PaymentFailedIntegration) Fail(string reason, bool isRetriable, DateTimeOffset failedAt)
    {
        var updated = this with
        {
            Status = PaymentStatus.Failed,
            FailureReason = reason,
            IsRetriable = isRetriable,
            ProcessedAt = failedAt
        };

        updated.PendingEvents.AddRange(PendingEvents);
        updated.PendingEvents.Add(new PaymentFailed(Id, reason, isRetriable, failedAt));

        var integrationMessage = new PaymentFailedIntegration(Id, OrderId, reason, isRetriable, failedAt);

        return (updated, integrationMessage);
    }

    #region Marten Event Sourcing

    /// <summary>
    /// Creates a Payment from a PaymentInitiated event (Marten event sourcing).
    /// Used by Marten to reconstruct aggregate state from events.
    /// </summary>
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
            false,
            @event.InitiatedAt,
            null);

    /// <summary>
    /// Applies a PaymentCaptured event to update state (Marten event sourcing).
    /// </summary>
    public Payment Apply(PaymentCaptured @event) =>
        this with
        {
            Status = PaymentStatus.Captured,
            TransactionId = @event.TransactionId,
            ProcessedAt = @event.CapturedAt
        };

    /// <summary>
    /// Applies a PaymentFailed event to update state (Marten event sourcing).
    /// </summary>
    public Payment Apply(PaymentFailed @event) =>
        this with
        {
            Status = PaymentStatus.Failed,
            FailureReason = @event.FailureReason,
            IsRetriable = @event.IsRetriable,
            ProcessedAt = @event.FailedAt
        };

    #endregion
}
