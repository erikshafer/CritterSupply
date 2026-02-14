using FluentValidation;
using Storefront.Clients;
using Wolverine.Http;

namespace Storefront.Api.Commands;

/// <summary>
/// BFF command to remove item from cart
/// Delegates to Shopping BC
/// </summary>
public sealed record RemoveItemFromCart(
    Guid CartId,
    string Sku);

public sealed class RemoveItemFromCartValidator : AbstractValidator<RemoveItemFromCart>
{
    public RemoveItemFromCartValidator()
    {
        RuleFor(x => x.CartId).NotEmpty();
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(50);
    }
}

public static class RemoveItemFromCartHandler
{
    [WolverineDelete("/api/storefront/carts/{cartId}/items/{sku}")]
    public static async Task<IResult> Handle(
        Guid cartId,
        string sku,
        IShoppingClient shoppingClient,
        CancellationToken ct)
    {
        try
        {
            await shoppingClient.RemoveItemAsync(cartId, sku, ct);
            return Results.NoContent(); // 204 - Item removed successfully
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Results.NotFound(new { message = "Cart or item not found" });
        }
        catch (HttpRequestException ex)
        {
            return Results.Problem(
                title: "Failed to remove item from cart",
                detail: ex.Message,
                statusCode: (int)(ex.StatusCode ?? System.Net.HttpStatusCode.InternalServerError));
        }
    }
}
