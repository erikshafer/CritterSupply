using FluentValidation;
using Wolverine.Http;

namespace Storefront.Api.Commands;

/// <summary>
/// BFF command to add item to cart
/// Delegates to Shopping BC
/// </summary>
public sealed record AddItemToCart(
    Guid CartId,
    string Sku,
    int Quantity,
    decimal UnitPrice);

public sealed class AddItemToCartValidator : AbstractValidator<AddItemToCart>
{
    public AddItemToCartValidator()
    {
        RuleFor(x => x.CartId).NotEmpty();
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.UnitPrice).GreaterThan(0);
    }
}

public static class AddItemToCartHandler
{
    [WolverinePost("/api/storefront/carts/{cartId}/items")]
    public static async Task<IResult> Handle(
        Guid cartId,
        AddItemToCartRequest request,
        IHttpClientFactory httpClientFactory,
        CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("ShoppingClient");

        var response = await client.PostAsJsonAsync(
            $"/api/carts/{cartId}/items",
            new
            {
                sku = request.Sku,
                quantity = request.Quantity,
                unitPrice = request.UnitPrice
            },
            ct);

        if (response.IsSuccessStatusCode)
        {
            return Results.NoContent(); // 204 - Item added successfully
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Results.NotFound(new { message = "Cart not found" });
        }

        return Results.Problem(
            title: "Failed to add item to cart",
            statusCode: (int)response.StatusCode);
    }
}

/// <summary>
/// Request body for adding item to cart
/// </summary>
public sealed record AddItemToCartRequest(
    string Sku,
    int Quantity,
    decimal UnitPrice);
