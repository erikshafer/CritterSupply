namespace Backoffice.Composition;

/// <summary>
/// Composed view for CS agents: customer detail with addresses and order history
/// </summary>
public sealed record CustomerDetailView(
    Guid CustomerId,
    string Email,
    string FirstName,
    string LastName,
    DateTime CreatedAt,
    IReadOnlyList<CustomerAddressView> Addresses,
    IReadOnlyList<OrderSummaryView> Orders);

/// <summary>
/// Customer address view for CS agents
/// </summary>
public sealed record CustomerAddressView(
    Guid AddressId,
    string Nickname,
    string AddressLine1,
    string? AddressLine2,
    string City,
    string StateOrProvince,
    string PostalCode,
    string Country,
    string AddressType,
    bool IsDefault);
