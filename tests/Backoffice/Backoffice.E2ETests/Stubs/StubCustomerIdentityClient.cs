using System.Net;
using Backoffice.Clients;

namespace Backoffice.E2ETests.Stubs;

/// <summary>
/// Stub implementation of ICustomerIdentityClient for E2E tests.
/// Returns in-memory test data configured per scenario.
/// </summary>
public sealed class StubCustomerIdentityClient : ICustomerIdentityClient
{
    private readonly Dictionary<Guid, CustomerDto> _customers = new();
    private readonly Dictionary<Guid, List<CustomerAddressDto>> _addresses = new();
    private readonly Dictionary<string, Guid> _emailToCustomerId = new();

    /// <summary>
    /// When true, all API methods will throw HttpRequestException with 401 Unauthorized.
    /// Used by SessionExpirySteps to simulate session expiry.
    /// </summary>
    public bool SimulateSessionExpired { get; set; }

    public void AddCustomer(Guid customerId, string email, string fullName)
    {
        var nameParts = fullName.Split(' ', 2);
        var firstName = nameParts.Length > 0 ? nameParts[0] : fullName;
        var lastName = nameParts.Length > 1 ? nameParts[1] : string.Empty;

        _customers[customerId] = new CustomerDto(
            customerId,
            email,
            firstName,
            lastName,
            PhoneNumber: null,
            DateTime.UtcNow.AddDays(-30));

        _emailToCustomerId[email.ToLowerInvariant()] = customerId;
    }

    public void AddAddress(Guid customerId, Guid addressId, string nickname, bool isDefault = false)
    {
        if (!_addresses.ContainsKey(customerId))
            _addresses[customerId] = new List<CustomerAddressDto>();

        _addresses[customerId].Add(new CustomerAddressDto(
            addressId,
            customerId,
            nickname,
            "123 Main St",
            null,
            "Seattle",
            "WA",
            "98101",
            "USA",
            "Shipping",
            isDefault));
    }

    public Task<CustomerDto?> GetCustomerByEmailAsync(string email, CancellationToken ct = default)
    {
        if (SimulateSessionExpired)
            throw new HttpRequestException("Session expired", null, HttpStatusCode.Unauthorized);

        var customerId = _emailToCustomerId.GetValueOrDefault(email.ToLowerInvariant());
        var customer = customerId != Guid.Empty ? _customers.GetValueOrDefault(customerId) : null;
        return Task.FromResult(customer);
    }

    public Task<CustomerDto?> GetCustomerAsync(Guid customerId, CancellationToken ct = default)
    {
        if (SimulateSessionExpired)
            throw new HttpRequestException("Session expired", null, HttpStatusCode.Unauthorized);

        return Task.FromResult(_customers.GetValueOrDefault(customerId));
    }

    public Task<IReadOnlyList<CustomerAddressDto>> GetCustomerAddressesAsync(
        Guid customerId,
        CancellationToken ct = default)
    {
        if (SimulateSessionExpired)
            throw new HttpRequestException("Session expired", null, HttpStatusCode.Unauthorized);

        var addresses = _addresses.GetValueOrDefault(customerId) ?? new List<CustomerAddressDto>();
        return Task.FromResult<IReadOnlyList<CustomerAddressDto>>(addresses);
    }

    public void Clear()
    {
        _customers.Clear();
        _addresses.Clear();
        _emailToCustomerId.Clear();
    }
}
