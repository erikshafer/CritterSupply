using Storefront.Clients;

namespace Storefront.IntegrationTests.Stubs;

/// <summary>
/// Stub implementation of ICustomerIdentityClient for testing
/// Returns predefined test data without making real HTTP calls
/// </summary>
public class StubCustomerIdentityClient : ICustomerIdentityClient
{
    private readonly List<CustomerAddressDto> _addresses = new();

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
}
