using FluentValidation;
using Storefront.Clients;
using Wolverine.Http;

namespace Storefront.Api.Commands;

/// <summary>
/// BFF command to complete checkout
/// Delegates to Orders BC
/// </summary>
public sealed record CompleteCheckout(Guid CheckoutId);

public sealed class CompleteCheckoutValidator : AbstractValidator<CompleteCheckout>
{
    public CompleteCheckoutValidator()
    {
        RuleFor(x => x.CheckoutId).NotEmpty();
    }
}

public static class CompleteCheckoutHandler
{
    [WolverinePost("/api/storefront/checkouts/{checkoutId}/complete")]
    public static async Task<IResult> Handle(
        Guid checkoutId,
        IOrdersClient ordersClient,
        CancellationToken ct)
    {
        try
        {
            var orderId = await ordersClient.CompleteCheckoutAsync(checkoutId, ct);
            return Results.Ok(new { orderId });
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
                title: "Failed to complete checkout",
                detail: ex.Message,
                statusCode: (int)(ex.StatusCode ?? System.Net.HttpStatusCode.InternalServerError));
        }
    }
}
