using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Storefront.Clients;
using Wolverine.Http;

namespace Storefront.Api.Commands;

/// <summary>
/// BFF command to add item to cart
/// Delegates to Shopping BC (which fetches server-authoritative price from Pricing BC)
/// </summary>
public sealed record AddItemToCart(
    Guid CartId,
    string Sku,
    int Quantity);

public sealed class AddItemToCartValidator : AbstractValidator<AddItemToCart>
{
    public AddItemToCartValidator()
    {
        RuleFor(x => x.CartId).NotEmpty();
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Quantity).GreaterThan(0);
    }
}

public static class AddItemToCartHandler
{
    [WolverinePost("/api/storefront/carts/{cartId}/items")]
    [Authorize]
    public static async Task<IResult> Handle(
        Guid cartId,
        AddItemToCartRequest request,
        IShoppingClient shoppingClient,
        CancellationToken ct)
    {
        try
        {
            await shoppingClient.AddItemAsync(cartId, request.Sku, request.Quantity, ct);
            return Results.NoContent(); // 204 - Item added successfully
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Results.NotFound(new { message = "Cart not found" });
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            // Price unavailable or product not available for purchase
            return Results.BadRequest(new { message = ex.Message });
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
/// Price is no longer accepted from client - Shopping BC fetches from Pricing BC
/// </summary>
public sealed record AddItemToCartRequest(
    string Sku,
    int Quantity);
