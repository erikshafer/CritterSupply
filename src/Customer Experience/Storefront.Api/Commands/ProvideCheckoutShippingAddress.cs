using Storefront.Clients;
using Wolverine.Http;

namespace Storefront.Api.Commands;

/// <summary>
/// BFF command to provide a shipping address for an in-progress checkout.
/// Routes through the BFF so all Checkout.razor calls use a single backend origin (StorefrontApi).
/// Delegates to Orders BC via IOrdersClient.
/// </summary>
public static class ProvideCheckoutShippingAddressHandler
{
    [WolverinePost("/api/storefront/checkouts/{checkoutId}/shipping-address")]
    public static async Task<IResult> Handle(
        Guid checkoutId,
        ProvideCheckoutShippingAddressRequest request,
        IOrdersClient ordersClient,
        CancellationToken ct)
    {
        try
        {
            await ordersClient.ProvideShippingAddressAsync(
                checkoutId,
                request.AddressLine1,
                request.AddressLine2,
                request.City,
                request.StateOrProvince,
                request.PostalCode,
                request.Country,
                ct);
            return Results.Ok();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Results.NotFound(new { message = "Checkout not found" });
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (HttpRequestException ex)
        {
            return Results.Problem(
                title: "Failed to save shipping address",
                detail: ex.Message,
                statusCode: (int)(ex.StatusCode ?? System.Net.HttpStatusCode.InternalServerError));
        }
    }
}

/// <summary>
/// Request body for providing a checkout shipping address.
/// </summary>
public sealed record ProvideCheckoutShippingAddressRequest(
    string AddressLine1,
    string? AddressLine2,
    string City,
    string StateOrProvince,
    string PostalCode,
    string Country);
