using FluentValidation;
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
        IHttpClientFactory httpClientFactory,
        CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("ShoppingClient");

        var response = await client.DeleteAsync(
            $"/api/carts/{cartId}/items/{sku}",
            ct);

        if (response.IsSuccessStatusCode)
        {
            return Results.NoContent(); // 204 - Item removed successfully
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Results.NotFound(new { message = "Cart or item not found" });
        }

        return Results.Problem(
            title: "Failed to remove item from cart",
            statusCode: (int)response.StatusCode);
    }
}
