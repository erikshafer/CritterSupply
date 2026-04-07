namespace Messages.Contracts.Fulfillment;

/// <summary>
/// Integration message from Fulfillment to Customer Experience and Correspondence BCs.
/// Published when a single delivery attempt fails. Includes attempt number so consumers
/// can differentiate early attempts (1, 2) from the final attempt (3) before return-to-sender.
/// </summary>
public sealed record DeliveryAttemptFailed(
    Guid OrderId,
    Guid ShipmentId,
    int AttemptNumber,
    string Carrier,
    string ExceptionCode,
    string ExceptionDescription,
    DateTimeOffset AttemptDate);
