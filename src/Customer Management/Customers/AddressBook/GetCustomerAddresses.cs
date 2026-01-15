using Marten;
using Wolverine.Http;

namespace Customers.AddressBook;

/// <summary>
/// Query to retrieve all addresses for a customer.
/// Optionally filter by address type (Shipping, Billing, Both).
/// </summary>
public sealed record GetCustomerAddresses(
    Guid CustomerId,
    AddressType? Type = null);

/// <summary>
/// Handler for GetCustomerAddresses query.
/// Returns all addresses for a customer, optionally filtered by type.
/// </summary>
public static class GetCustomerAddressesHandler
{
    [WolverineGet("/api/customers/{customerId}/addresses")]
    public static async Task<IReadOnlyList<CustomerAddress>> Handle(
        Guid customerId,
        AddressType? type,
        IDocumentSession session,
        CancellationToken ct)
    {
        var addressQuery = session.Query<CustomerAddress>()
            .Where(a => a.CustomerId == customerId);

        if (type.HasValue)
        {
            addressQuery = addressQuery.Where(a => a.Type == type.Value || a.Type == AddressType.Both);
        }

        return await addressQuery.ToListAsync(ct);
    }
}
