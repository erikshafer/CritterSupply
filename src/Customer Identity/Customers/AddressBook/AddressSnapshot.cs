namespace CustomerIdentity.AddressBook;

/// <summary>
/// Immutable snapshot of an address at a point in time.
/// Used for integration with Shopping BC (checkout) and Orders BC (order placement).
/// This snapshot preserves the address as it was when the transaction occurred,
/// ensuring temporal consistency even if the customer updates the address later.
/// </summary>
public sealed record AddressSnapshot(
    string AddressLine1,
    string? AddressLine2,
    string City,
    string StateOrProvince,
    string PostalCode,
    string Country);
