namespace Messages.Contracts.Returns;

/// <summary>
/// Integration message published by Returns BC when a return is approved.
/// Customer Experience BC updates return status UI; Notifications BC sends approval email.
/// </summary>
public sealed record ReturnApproved(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    decimal EstimatedRefundAmount,
    decimal RestockingFeeAmount,
    DateTimeOffset ShipByDeadline,
    DateTimeOffset ApprovedAt);
