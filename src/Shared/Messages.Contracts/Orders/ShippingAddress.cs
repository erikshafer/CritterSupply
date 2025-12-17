namespace Messages.Contracts.Orders;

/// <summary>
/// The delivery destination for an order.
/// Part of OrderPlaced integration message.
/// </summary>
public sealed record ShippingAddress(
    string Street,
    string? Street2,
    string City,
    string State,
    string PostalCode,
    string Country);
