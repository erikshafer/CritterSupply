using Storefront.Clients;
using Wolverine.Http;

namespace Storefront.Api.Commands;

/// <summary>
/// BFF command to add a new shipping address for a customer during checkout.
/// Routes through the BFF so Checkout.razor uses a single backend origin (StorefrontApi).
/// Delegates to Customer Identity BC via ICustomerIdentityClient.
/// </summary>
public static class AddCustomerAddressHandler
{
    [WolverinePost("/api/storefront/customers/{customerId}/addresses")]
    public static async Task<IResult> Handle(
        Guid customerId,
        AddCustomerAddressRequest request,
        ICustomerIdentityClient identityClient,
        CancellationToken ct)
    {
        try
        {
            var addRequest = new AddAddressRequest(
                request.Nickname,
                request.AddressLine1,
                request.AddressLine2,
                request.City,
                request.StateOrProvince,
                request.PostalCode,
                request.Country);

            var addressId = await identityClient.AddAddressAsync(customerId, addRequest, ct);
            return Results.Created($"/api/storefront/customers/{customerId}/addresses/{addressId}", new { addressId });
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Results.NotFound(new { message = "Customer not found" });
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            return Results.Conflict(new { message = "Address with that nickname already exists" });
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (HttpRequestException ex)
        {
            return Results.Problem(
                title: "Failed to add address",
                detail: ex.Message,
                statusCode: (int)(ex.StatusCode ?? System.Net.HttpStatusCode.InternalServerError));
        }
    }
}

/// <summary>
/// Request body for adding a new customer address via the BFF.
/// </summary>
public sealed record AddCustomerAddressRequest(
    string Nickname,
    string AddressLine1,
    string? AddressLine2,
    string City,
    string StateOrProvince,
    string PostalCode,
    string Country);
