using FluentValidation;
using Wolverine.Http;

namespace Storefront.Api.Commands;

/// <summary>
/// BFF command to change item quantity in cart
/// Delegates to Shopping BC
/// </summary>
public sealed record ChangeItemQuantity(
    Guid CartId,
    string Sku,
    int NewQuantity);

public sealed class ChangeItemQuantityValidator : AbstractValidator<ChangeItemQuantity>
{
    public ChangeItemQuantityValidator()
    {
        RuleFor(x => x.CartId).NotEmpty();
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(50);
        RuleFor(x => x.NewQuantity).GreaterThan(0);
    }
}

public static class ChangeItemQuantityHandler
{
    [WolverinePut("/api/storefront/carts/{cartId}/items/{sku}/quantity")]
    public static async Task<IResult> Handle(
        Guid cartId,
        string sku,
        ChangeItemQuantityRequest request,
        IHttpClientFactory httpClientFactory,
        CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("ShoppingClient");

        var response = await client.PutAsJsonAsync(
            $"/api/carts/{cartId}/items/{sku}/quantity",
            new
            {
                newQuantity = request.NewQuantity
            },
            ct);

        if (response.IsSuccessStatusCode)
        {
            return Results.NoContent(); // 204 - Quantity updated successfully
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Results.NotFound(new { message = "Cart or item not found" });
        }

        return Results.Problem(
            title: "Failed to update item quantity",
            statusCode: (int)response.StatusCode);
    }
}

/// <summary>
/// Request body for changing item quantity
/// </summary>
public sealed record ChangeItemQuantityRequest(int NewQuantity);
