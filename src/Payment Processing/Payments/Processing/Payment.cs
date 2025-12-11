using IntegrationMessages = Messages.Contracts.Payments;

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
    string? AuthorizationId,
    DateTimeOffset? AuthorizationExpiresAt,
    string? FailureReason,
    bool IsRetriable,
    DateTimeOffset InitiatedAt,
    DateTimeOffset? ProcessedAt,
    decimal TotalRefunded = 0m)
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
            null,
            null,
            false,
            initiatedAt,
            null,
            0m);

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
    /// Authorizes the payment (holds funds without capturing).
    /// Updates status to Authorized and returns integration message for Orders context.
    /// </summary>
    public (Payment, IntegrationMessages.PaymentAuthorized) Authorize(string authorizationId, DateTimeOffset authorizedAt, DateTimeOffset expiresAt)
    {
        var updated = this with
        {
            Status = PaymentStatus.Authorized,
            AuthorizationId = authorizationId,
            AuthorizationExpiresAt = expiresAt,
            ProcessedAt = authorizedAt
        };

        updated.PendingEvents.AddRange(PendingEvents);
        updated.PendingEvents.Add(new PaymentAuthorized(Id, authorizationId, authorizedAt));

        var integrationMessage = new IntegrationMessages.PaymentAuthorized(
            Id,
            OrderId,
            Amount,
            authorizationId,
            authorizedAt,
            expiresAt);

        return (updated, integrationMessage);
    }

    /// <summary>
    /// Captures the payment with the given transaction ID.
    /// Updates status to Captured and returns integration message for Orders context.
    /// </summary>
    public (Payment, IntegrationMessages.PaymentCaptured) Capture(string transactionId, DateTimeOffset capturedAt)
    {
        var updated = this with
        {
            Status = PaymentStatus.Captured,
            TransactionId = transactionId,
            ProcessedAt = capturedAt
        };

        updated.PendingEvents.AddRange(PendingEvents);
        updated.PendingEvents.Add(new PaymentCaptured(Id, transactionId, capturedAt));

        var integrationMessage = new IntegrationMessages.PaymentCaptured(Id, OrderId, Amount, transactionId, capturedAt);

        return (updated, integrationMessage);
    }

    /// <summary>
    /// Captures a previously authorized payment.
    /// Updates status to Captured and returns integration message for Orders context.
    /// </summary>
    public (Payment, IntegrationMessages.PaymentCaptured) CaptureAuthorized(string transactionId, DateTimeOffset capturedAt)
    {
        var updated = this with
        {
            Status = PaymentStatus.Captured,
            TransactionId = transactionId,
            ProcessedAt = capturedAt
        };

        updated.PendingEvents.AddRange(PendingEvents);
        updated.PendingEvents.Add(new PaymentCaptured(Id, transactionId, capturedAt));

        var integrationMessage = new IntegrationMessages.PaymentCaptured(Id, OrderId, Amount, transactionId, capturedAt);

        return (updated, integrationMessage);
    }

    /// <summary>
    /// Fails the payment with the given reason.
    /// Updates status to Failed and returns integration message for Orders context.
    /// </summary>
    public (Payment, IntegrationMessages.PaymentFailed) Fail(string reason, bool isRetriable, DateTimeOffset failedAt)
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

        var integrationMessage = new IntegrationMessages.PaymentFailed(Id, OrderId, reason, isRetriable, failedAt);

        return (updated, integrationMessage);
    }

    /// <summary>
    /// Gets the remaining amount available for refund.
    /// </summary>
    public decimal RefundableAmount => Amount - TotalRefunded;

    /// <summary>
    /// Processes a refund against this payment.
    /// Updates total refunded and returns integration message for Orders context.
    /// </summary>
    public (Payment, PaymentRefunded, IntegrationMessages.RefundCompleted) Refund(decimal refundAmount, string refundTransactionId, DateTimeOffset refundedAt)
    {
        var newTotalRefunded = TotalRefunded + refundAmount;
        var newStatus = newTotalRefunded >= Amount ? PaymentStatus.Refunded : Status;

        var updated = this with
        {
            Status = newStatus,
            TotalRefunded = newTotalRefunded
        };

        var domainEvent = new PaymentRefunded(Id, refundAmount, newTotalRefunded, refundTransactionId, refundedAt);
        updated.PendingEvents.AddRange(PendingEvents);
        updated.PendingEvents.Add(domainEvent);

        var integrationMessage = new IntegrationMessages.RefundCompleted(Id, OrderId, refundAmount, refundTransactionId, refundedAt);

        return (updated, domainEvent, integrationMessage);
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
            null,
            null,
            false,
            @event.InitiatedAt,
            null,
            0m);

    /// <summary>
    /// Applies a PaymentAuthorized event to update state (Marten event sourcing).
    /// </summary>
    public Payment Apply(PaymentAuthorized @event) =>
        this with
        {
            Status = PaymentStatus.Authorized,
            AuthorizationId = @event.AuthorizationId,
            AuthorizationExpiresAt = @event.AuthorizedAt.AddDays(7), // 7-day expiration
            ProcessedAt = @event.AuthorizedAt
        };

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

    /// <summary>
    /// Applies a PaymentRefunded event to update state (Marten event sourcing).
    /// </summary>
    public Payment Apply(PaymentRefunded @event) =>
        this with
        {
            Status = @event.TotalRefunded >= Amount ? PaymentStatus.Refunded : Status,
            TotalRefunded = @event.TotalRefunded
        };

    #endregion
}
