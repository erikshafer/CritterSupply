using Microsoft.AspNetCore.Authorization;
using Storefront.Clients;
using Wolverine.Http;

namespace Storefront.Api.Commands;

/// <summary>
/// BFF command to select a shipping method for an in-progress checkout.
/// Routes through the BFF so all Checkout.razor calls use a single backend origin (StorefrontApi).
/// Delegates to Orders BC via IOrdersClient.
/// </summary>
public static class SelectCheckoutShippingMethodHandler
{
    [WolverinePost("/api/storefront/checkouts/{checkoutId}/shipping-method")]
    [Authorize]
    public static async Task<IResult> Handle(
        Guid checkoutId,
        SelectCheckoutShippingMethodRequest request,
        IOrdersClient ordersClient,
        CancellationToken ct)
    {
        try
        {
            await ordersClient.SelectShippingMethodAsync(
                checkoutId,
                request.ShippingMethod,
                request.ShippingCost,
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
                title: "Failed to save shipping method",
                detail: ex.Message,
                statusCode: (int)(ex.StatusCode ?? System.Net.HttpStatusCode.InternalServerError));
        }
    }
}

/// <summary>
/// Request body for selecting a checkout shipping method.
/// </summary>
public sealed record SelectCheckoutShippingMethodRequest(
    string ShippingMethod,
    decimal ShippingCost);
