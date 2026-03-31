using Backoffice.Clients;
using Backoffice.Composition;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace Backoffice.Api.Queries;

/// <summary>
/// Query to get customer detail view by ID (CS workflow: customer detail)
/// Composes data from Customer Identity BC + Orders BC
/// </summary>
public static class GetCustomerDetailViewQuery
{
    [WolverineGet("/api/backoffice/customers/{customerId}")]
    [Authorize(Policy = "CustomerService")]
    public static async Task<IResult> Handle(
        Guid customerId,
        ICustomerIdentityClient customerClient,
        IOrdersClient ordersClient,
        CancellationToken ct)
    {
        // Query Customer Identity BC for customer
        var customer = await customerClient.GetCustomerAsync(customerId, ct);

        if (customer is null)
            return Results.NotFound(new { message = "Customer not found" });

        // Query Customer Identity BC for addresses
        var addresses = await customerClient.GetCustomerAddressesAsync(customerId, ct);

        // Query Orders BC for order history
        var orders = await ordersClient.GetOrdersAsync(customerId, limit: 50, ct);

        // Map addresses to composition view
        var addressViews = addresses.Select(a => new CustomerAddressView(
            a.Id,
            a.Nickname,
            a.AddressLine1,
            a.AddressLine2,
            a.City,
            a.StateOrProvince,
            a.PostalCode,
            a.Country,
            a.AddressType,
            a.IsDefault)).ToList();

        // Map orders to composition view
        var orderSummaries = orders.Select(o => new OrderSummaryView(
            o.Id,
            o.PlacedAt,
            o.Status,
            o.TotalAmount)).ToList();

        var view = new CustomerDetailView(
            customer.Id,
            customer.Email,
            customer.FirstName,
            customer.LastName,
            customer.CreatedAt,
            addressViews,
            orderSummaries);

        return Results.Ok(view);
    }
}
