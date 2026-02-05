namespace Storefront.Clients;

/// <summary>
/// HTTP client for querying Customer Identity BC
/// </summary>
public interface ICustomerIdentityClient
{
    Task<IReadOnlyList<CustomerAddressDto>> GetCustomerAddressesAsync(
        Guid customerId,
        string? addressType = null,
        CancellationToken ct = default);
}

/// <summary>
/// Customer address DTO from Customer Identity BC
/// </summary>
public sealed record CustomerAddressDto(
    Guid Id,
    Guid CustomerId,
    string Nickname,
    string AddressLine1,
    string? AddressLine2,
    string City,
    string StateOrProvince,
    string PostalCode,
    string Country,
    string AddressType,  // "Shipping", "Billing", "Both"
    bool IsDefault);
