using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Wolverine.Http;

namespace Backoffice.Api.Queries;

/// <summary>
/// Query: Search customers by name, email, or customer ID.
/// Used by Customer Service agents for customer lookup.
/// </summary>
public static class SearchCustomers
{
    [WolverineGet("/api/backoffice/customers/search")]
    [Authorize(Policy = "CustomerService")]
    public static Ok<CustomerSearchResponse> Get(string q)
    {
        // STUB: Return hardcoded search results for now
        // Will be replaced with real Customer Identity BC query in Phase 3
        var customers = new List<CustomerSearchResult>
        {
            new(
                CustomerId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
                FirstName: "Alice",
                LastName: "Anderson",
                Email: "alice.anderson@example.com",
                Status: "Active",
                TotalOrders: 15,
                CreatedAt: DateTimeOffset.UtcNow.AddMonths(-6)),
            new(
                CustomerId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
                FirstName: "Bob",
                LastName: "Baker",
                Email: "bob.baker@example.com",
                Status: "Active",
                TotalOrders: 3,
                CreatedAt: DateTimeOffset.UtcNow.AddMonths(-2)),
            new(
                CustomerId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
                FirstName: "Carol",
                LastName: "Clark",
                Email: "carol.clark@example.com",
                Status: "Inactive",
                TotalOrders: 0,
                CreatedAt: DateTimeOffset.UtcNow.AddYears(-1))
        };

        // Simple search filter (case-insensitive contains)
        var filtered = customers.Where(c =>
            c.FirstName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            c.LastName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            c.Email.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            c.CustomerId.ToString().Contains(q, StringComparison.OrdinalIgnoreCase)
        ).ToList();

        var response = new CustomerSearchResponse(
            Customers: filtered,
            TotalCount: filtered.Count,
            SearchedAt: DateTimeOffset.UtcNow);

        return TypedResults.Ok(response);
    }
}

public sealed record CustomerSearchResponse(
    IReadOnlyList<CustomerSearchResult> Customers,
    int TotalCount,
    DateTimeOffset SearchedAt);

public sealed record CustomerSearchResult(
    Guid CustomerId,
    string FirstName,
    string LastName,
    string Email,
    string Status,
    int TotalOrders,
    DateTimeOffset CreatedAt);
