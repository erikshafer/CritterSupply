namespace Storefront.Clients;

/// <summary>
/// HTTP client for querying Customer Identity BC (addresses + authentication)
/// </summary>
public interface ICustomerIdentityClient
{
    // Address management
    Task<IReadOnlyList<CustomerAddressDto>> GetCustomerAddressesAsync(
        Guid customerId,
        string? addressType = null,
        CancellationToken ct = default);

    // Authentication
    Task<LoginResponse?> LoginAsync(string email, string password, CancellationToken ct = default);
    Task LogoutAsync(CancellationToken ct = default);
    Task<CurrentUserResponse?> GetCurrentUserAsync(CancellationToken ct = default);
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

/// <summary>
/// Response from Customer Identity BC login endpoint
/// </summary>
public sealed record LoginResponse(Guid CustomerId, string Email, string FirstName, string LastName);

/// <summary>
/// Response from Customer Identity BC current user endpoint
/// </summary>
public sealed record CurrentUserResponse(Guid CustomerId, string Email, string FirstName, string LastName);
