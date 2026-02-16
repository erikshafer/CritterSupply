namespace Orders.Placement;

/// <summary>
/// The delivery destination for an order.
/// </summary>
public sealed record ShippingAddress(
    string Street,
    string? Street2,
    string City,
    string State,
    string PostalCode,
    string Country);
