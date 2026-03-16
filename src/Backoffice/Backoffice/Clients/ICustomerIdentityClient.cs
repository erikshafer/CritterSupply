namespace Backoffice.Clients;

/// <summary>
/// HTTP client for querying Customer Identity BC (admin use)
/// </summary>
public interface ICustomerIdentityClient
{
    /// <summary>
    /// Search customer by email (CS workflow: customer lookup)
    /// </summary>
    Task<CustomerDto?> GetCustomerByEmailAsync(string email, CancellationToken ct = default);

    /// <summary>
    /// Get customer by ID
    /// </summary>
    Task<CustomerDto?> GetCustomerAsync(Guid customerId, CancellationToken ct = default);

    /// <summary>
    /// Get all addresses for a customer (CS workflow: return address verification)
    /// </summary>
    Task<IReadOnlyList<CustomerAddressDto>> GetCustomerAddressesAsync(
        Guid customerId,
        CancellationToken ct = default);
}

/// <summary>
/// Customer DTO from Customer Identity BC
/// </summary>
public sealed record CustomerDto(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    DateTime CreatedAt);

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
    string AddressType,
    bool IsDefault);
