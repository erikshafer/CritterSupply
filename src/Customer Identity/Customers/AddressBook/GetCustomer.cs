using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Wolverine.Http;

namespace CustomerIdentity.AddressBook;

/// <summary>
/// Query to retrieve a customer by ID.
/// </summary>
public sealed record GetCustomer(Guid CustomerId);

/// <summary>
/// Response DTO for customer details.
/// </summary>
public sealed record CustomerResponse(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    DateTimeOffset CreatedAt);

/// <summary>
/// Handler for GetCustomer query.
/// Retrieves customer details without addresses.
/// </summary>
public static class GetCustomerHandler
{
    [WolverineGet("/api/customers/{customerId}")]
    public static async Task<IResult> Handle(
        Guid customerId,
        CustomerIdentityDbContext dbContext,
        CancellationToken ct)
    {
        var customer = await dbContext.Customers
            .AsNoTracking()
            .Where(c => c.Id == customerId)
            .Select(c => new CustomerResponse(
                c.Id,
                c.Email,
                c.FirstName,
                c.LastName,
                c.CreatedAt))
            .FirstOrDefaultAsync(ct);

        if (customer is null)
            return Results.NotFound();

        return Results.Ok(customer);
    }
}
