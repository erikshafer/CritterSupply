using Backoffice.Clients;
using Backoffice.Composition;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Wolverine.Http;

namespace Backoffice.Api.Queries;

/// <summary>
/// Query to get customer service view by email (CS workflow: customer search)
/// Composes data from Customer Identity BC + Orders BC
/// </summary>
public static class GetCustomerServiceViewQuery
{
    [WolverineGet("/api/backoffice/customers/search")]
    [Authorize(Policy = "CustomerService")]
    public static async Task<IResult> Handle(
        string email,
        ICustomerIdentityClient customerClient,
        IOrdersClient ordersClient,
        CancellationToken ct)
    {
        // Query Customer Identity BC for customer
        var customer = await customerClient.GetCustomerByEmailAsync(email, ct);

        if (customer is null)
            return Results.NotFound(new { message = "Customer not found" });

        // Query Orders BC for order history
        var orders = await ordersClient.GetOrdersAsync(customer.Id, limit: 50, ct);

        // Map to composition view
        var orderSummaries = orders.Select(o => new OrderSummaryView(
            o.Id,
            o.PlacedAt,
            o.Status,
            o.TotalAmount)).ToList();

        var view = new CustomerServiceView(
            customer.Id,
            customer.Email,
            customer.FirstName,
            customer.LastName,
            customer.CreatedAt,
            orderSummaries);

        return Results.Ok(view);
    }
}
