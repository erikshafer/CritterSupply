using Storefront.Clients;

namespace Storefront.Api.IntegrationTests.Stubs;

/// <summary>
/// Stub implementation of ICustomerIdentityClient for testing
/// Returns predefined test data without making real HTTP calls
/// </summary>
public class StubCustomerIdentityClient : ICustomerIdentityClient
{
    private readonly List<CustomerAddressDto> _addresses = new();
    private readonly Dictionary<Guid, System.Net.HttpStatusCode> _addAddressFailures = new();

    public void AddAddress(CustomerAddressDto address)
    {
        _addresses.Add(address);
    }

    public Task<IReadOnlyList<CustomerAddressDto>> GetCustomerAddressesAsync(
        Guid customerId,
        string? addressType = null,
        CancellationToken ct = default)
    {
        var query = _addresses.Where(a => a.CustomerId == customerId);

        if (!string.IsNullOrEmpty(addressType))
        {
            query = query.Where(a => a.AddressType.Equals(addressType, StringComparison.OrdinalIgnoreCase) ||
                                     a.AddressType.Equals("Both", StringComparison.OrdinalIgnoreCase));
        }

        var result = query.ToList();
        return Task.FromResult<IReadOnlyList<CustomerAddressDto>>(result);
    }

    // Authentication methods (stubs for testing)
    public Task<LoginResponse?> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        // Stub: Return test user
        var response = new LoginResponse(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), email, "Test", "User");
        return Task.FromResult<LoginResponse?>(response);
    }

    public Task LogoutAsync(CancellationToken ct = default)
    {
        // Stub: No-op
        return Task.CompletedTask;
    }

    public Task<CurrentUserResponse?> GetCurrentUserAsync(CancellationToken ct = default)
    {
        // Stub: Return test user
        var response = new CurrentUserResponse(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "test@critter.test", "Test", "User");
        return Task.FromResult<CurrentUserResponse?>(response);
    }

    public Task<Guid> AddAddressAsync(
        Guid customerId,
        AddAddressRequest request,
        CancellationToken ct = default)
    {
        if (_addAddressFailures.TryGetValue(customerId, out var statusCode))
            throw new HttpRequestException($"Simulated {statusCode}", null, statusCode);

        var addressId = Guid.CreateVersion7();
        var dto = new CustomerAddressDto(
            addressId,
            customerId,
            request.Nickname,
            request.AddressLine1,
            request.AddressLine2,
            request.City,
            request.StateOrProvince,
            request.PostalCode,
            request.Country,
            "Shipping",
            true,
            $"{request.AddressLine1}, {request.City}, {request.StateOrProvince} {request.PostalCode}, {request.Country}");
        _addresses.Add(dto);
        return Task.FromResult(addressId);
    }

    /// <summary>
    /// Configure AddAddressAsync to throw an HttpRequestException for a specific customer.
    /// Used to simulate 404 (customer not found) or 409 (nickname conflict) errors.
    /// </summary>
    public void ConfigureAddAddressFailure(Guid customerId, System.Net.HttpStatusCode statusCode)
    {
        _addAddressFailures[customerId] = statusCode;
    }

    /// <summary>
    /// Clear all configured test data (for test isolation)
    /// </summary>
    public void Clear()
    {
        _addresses.Clear();
        _addAddressFailures.Clear();
    }
}
