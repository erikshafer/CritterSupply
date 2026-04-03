using Microsoft.EntityFrameworkCore;
using Wolverine.Http;

namespace CustomerIdentity.AddressBook;

/// <summary>
/// Query to retrieve all addresses for a customer.
/// Optionally filter by address type (Shipping, Billing, Both).
/// </summary>
public sealed record GetCustomerAddresses(
    Guid CustomerId,
    AddressType? Type = null);

/// <summary>
/// DTO for address details in list views.
/// Includes full address fields so BFF can forward to Orders BC without a second lookup.
/// </summary>
public sealed record AddressSummary(
    Guid Id,
    AddressType Type,
    string Nickname,
    string AddressLine1,
    string? AddressLine2,
    string City,
    string StateOrProvince,
    string PostalCode,
    string Country,
    string DisplayLine,
    bool IsDefault,
    bool IsVerified);

/// <summary>
/// Handler for GetCustomerAddresses query.
/// Returns all addresses for a customer, optionally filtered by type.
/// </summary>
public static class GetCustomerAddressesHandler
{
    [WolverineGet("/api/customers/{customerId}/addresses")]
    public static async Task<IReadOnlyList<AddressSummary>> Handle(
        Guid customerId,
        AddressType? type,
        CustomerIdentityDbContext dbContext,
        CancellationToken ct)
    {
        var addressQuery = dbContext.Addresses
            .Where(a => a.CustomerId == customerId);

        if (type.HasValue)
        {
            addressQuery = addressQuery.Where(a => a.Type == type.Value || a.Type == AddressType.Both);
        }

        var addresses = await addressQuery.ToListAsync(ct);

        return addresses
            .Select(a => new AddressSummary(
                a.Id,
                a.Type,
                a.Nickname,
                a.AddressLine1,
                a.AddressLine2,
                a.City,
                a.StateOrProvince,
                a.PostalCode,
                a.Country,
                a.DisplayLine,
                a.IsDefault,
                a.IsVerified))
            .ToList();
    }
}
