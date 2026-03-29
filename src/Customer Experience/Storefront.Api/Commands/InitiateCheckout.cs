using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Storefront.Clients;
using Wolverine.Http;

namespace Storefront.Api.Commands;

/// <summary>
/// BFF command to initiate checkout from cart
/// Delegates to Shopping BC
/// </summary>
public sealed record InitiateCheckout(Guid CartId);

public sealed class InitiateCheckoutValidator : AbstractValidator<InitiateCheckout>
{
    public InitiateCheckoutValidator()
    {
        RuleFor(x => x.CartId).NotEmpty();
    }
}

public static class InitiateCheckoutHandler
{
    [WolverinePost("/api/storefront/carts/{cartId}/checkout")]
    [Authorize]
    public static async Task<IResult> Handle(
        Guid cartId,
        IShoppingClient shoppingClient,
        CancellationToken ct)
    {
        try
        {
            var checkoutId = await shoppingClient.InitiateCheckoutAsync(cartId, ct);
            return Results.Ok(new { checkoutId });
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Results.NotFound(new { message = "Cart not found" });
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (HttpRequestException ex)
        {
            return Results.Problem(
                title: "Failed to initiate checkout",
                detail: ex.Message,
                statusCode: (int)(ex.StatusCode ?? System.Net.HttpStatusCode.InternalServerError));
        }
    }
}
