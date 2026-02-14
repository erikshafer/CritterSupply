using FluentValidation;
using Storefront.Clients;
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
        IShoppingClient shoppingClient,
        CancellationToken ct)
    {
        try
        {
            await shoppingClient.AddItemAsync(cartId, request.Sku, request.Quantity, request.UnitPrice, ct);
            return Results.NoContent(); // 204 - Item added successfully
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Results.NotFound(new { message = "Cart not found" });
        }
        catch (HttpRequestException ex)
        {
            return Results.Problem(
                title: "Failed to add item to cart",
                detail: ex.Message,
                statusCode: (int)(ex.StatusCode ?? System.Net.HttpStatusCode.InternalServerError));
        }
    }
}

/// <summary>
/// Request body for adding item to cart
/// </summary>
public sealed record AddItemToCartRequest(
    string Sku,
    int Quantity,
    decimal UnitPrice);
