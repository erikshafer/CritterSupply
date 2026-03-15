using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Wolverine.Http;

namespace CustomerIdentity.AddressBook;

/// <summary>
/// Query to retrieve a customer by email address.
/// Used by CS agents for customer lookup workflows.
/// </summary>
public sealed record GetCustomerByEmail(string Email);

/// <summary>
/// Handler for GetCustomerByEmail query.
/// Returns customer details or 404 if email not found.
/// </summary>
public static class GetCustomerByEmailHandler
{
    [WolverineGet("/api/customers")]
    public static async Task<IResult> Handle(
        string email,
        CustomerIdentityDbContext dbContext,
        CancellationToken ct)
    {
        var customer = await dbContext.Customers
            .AsNoTracking()
            .Where(c => c.Email == email)
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
