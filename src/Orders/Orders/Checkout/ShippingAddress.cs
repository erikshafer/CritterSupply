namespace Orders.Checkout;

public sealed record ShippingAddress(
    string AddressLine1,
    string? AddressLine2,
    string City,
    string StateOrProvince,
    string PostalCode,
    string Country);
