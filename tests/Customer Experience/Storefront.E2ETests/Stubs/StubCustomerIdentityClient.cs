using Storefront.Clients;

namespace Storefront.E2ETests.Stubs;

/// <summary>
/// Stub implementation of ICustomerIdentityClient for E2E testing.
/// Returns predefined address and auth data without making real HTTP calls.
/// </summary>
public sealed class StubCustomerIdentityClient : ICustomerIdentityClient
{
    private readonly List<CustomerAddressDto> _addresses = new();

    public void AddAddress(CustomerAddressDto address) => _addresses.Add(address);

    public Task<IReadOnlyList<CustomerAddressDto>> GetCustomerAddressesAsync(
        Guid customerId,
        string? addressType = null,
        CancellationToken ct = default)
    {
        var query = _addresses.Where(a => a.CustomerId == customerId);
        if (!string.IsNullOrEmpty(addressType))
            query = query.Where(a => a.AddressType.Equals(addressType, StringComparison.OrdinalIgnoreCase)
                                  || a.AddressType.Equals("Both", StringComparison.OrdinalIgnoreCase));

        return Task.FromResult<IReadOnlyList<CustomerAddressDto>>(query.ToList());
    }

    public Task<LoginResponse?> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        var response = new LoginResponse(WellKnownTestData.Customers.Alice, email, "Alice", "Testington");
        return Task.FromResult<LoginResponse?>(response);
    }

    public Task LogoutAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<CurrentUserResponse?> GetCurrentUserAsync(CancellationToken ct = default)
    {
        var response = new CurrentUserResponse(
            WellKnownTestData.Customers.Alice,
            WellKnownTestData.Customers.AliceEmail,
            WellKnownTestData.Customers.AliceFirstName,
            WellKnownTestData.Customers.AliceLastName);
        return Task.FromResult<CurrentUserResponse?>(response);
    }

    public void Clear() => _addresses.Clear();
}
