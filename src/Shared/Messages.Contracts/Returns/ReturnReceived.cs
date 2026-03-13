namespace Messages.Contracts.Returns;

/// <summary>
/// Integration message published by Returns BC when a return package is physically
/// received at the warehouse. Customer Experience BC shows "We received your package"
/// status; reduces customer anxiety during the return flow.
/// </summary>
public sealed record ReturnReceived(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    DateTimeOffset ReceivedAt);
