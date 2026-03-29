using Microsoft.AspNetCore.Authorization;
using Storefront.Clients;
using Wolverine.Http;

namespace Storefront.Api.Commands;

/// <summary>
/// BFF command to provide a payment method token for an in-progress checkout.
/// Routes through the BFF so all Checkout.razor calls use a single backend origin (StorefrontApi).
/// Delegates to Orders BC via IOrdersClient.
/// </summary>
public static class ProvideCheckoutPaymentMethodHandler
{
    [WolverinePost("/api/storefront/checkouts/{checkoutId}/payment-method")]
    [Authorize]
    public static async Task<IResult> Handle(
        Guid checkoutId,
        ProvideCheckoutPaymentMethodRequest request,
        IOrdersClient ordersClient,
        CancellationToken ct)
    {
        try
        {
            await ordersClient.ProvidePaymentMethodAsync(
                checkoutId,
                request.PaymentMethodToken,
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
                title: "Failed to save payment method",
                detail: ex.Message,
                statusCode: (int)(ex.StatusCode ?? System.Net.HttpStatusCode.InternalServerError));
        }
    }
}

/// <summary>
/// Request body for providing a checkout payment method.
/// </summary>
public sealed record ProvideCheckoutPaymentMethodRequest(string PaymentMethodToken);
