namespace Customers.AddressBook;

/// <summary>
/// Represents a customer's saved address for shipping or billing purposes.
/// Immutable record following modern C# patterns - use "with" expressions for updates.
/// </summary>
public sealed record CustomerAddress(
    Guid Id,
    Guid CustomerId,
    AddressType Type,
    string Nickname,
    string AddressLine1,
    string? AddressLine2,
    string City,
    string StateOrProvince,
    string PostalCode,
    string Country,
    bool IsDefault,
    bool IsVerified,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt)
{
    /// <summary>
    /// Indicates if this address can be used for shipping purposes.
    /// </summary>
    public bool IsShipping => Type is AddressType.Shipping or AddressType.Both;

    /// <summary>
    /// Indicates if this address can be used for billing purposes.
    /// </summary>
    public bool IsBilling => Type is AddressType.Billing or AddressType.Both;

    /// <summary>
    /// Returns a display-friendly single-line representation of the address.
    /// Example: "123 Main St, Seattle, WA 98101, USA"
    /// </summary>
    public string DisplayLine =>
        AddressLine2 is null
            ? $"{AddressLine1}, {City}, {StateOrProvince} {PostalCode}, {Country}"
            : $"{AddressLine1}, {AddressLine2}, {City}, {StateOrProvince} {PostalCode}, {Country}";
}
