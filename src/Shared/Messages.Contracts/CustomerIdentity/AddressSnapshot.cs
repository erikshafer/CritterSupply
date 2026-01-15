namespace Messages.Contracts.CustomerIdentity;

/// <summary>
/// Immutable snapshot of an address at a point in time.
/// Created by Customer Identity BC when other BCs request address data.
/// Ensures temporal consistency - historical records preserve address as it was at transaction time.
/// </summary>
public sealed record AddressSnapshot(
    string AddressLine1,
    string? AddressLine2,
    string City,
    string StateOrProvince,
    string PostalCode,
    string Country);
