using Microsoft.AspNetCore.Http;
using Storefront.Clients;
using Storefront.Composition;
using Wolverine.Http;

namespace Storefront.Queries;

/// <summary>
/// Query to get composed cart view (Shopping BC + Catalog BC)
/// </summary>
public sealed record GetCartView(Guid CartId);

public static class GetCartViewHandler
{
    [WolverineGet("/api/storefront/carts/{cartId}")]
    public static async Task<IResult> Handle(
        Guid cartId,
        IShoppingClient shoppingClient,
        ICatalogClient catalogClient,
        CancellationToken ct)
    {
        try
        {
            // Query Shopping BC for cart state
            var cart = await shoppingClient.GetCartAsync(cartId, ct);

            // Enrich line items with product details from Catalog BC
            var enrichedItems = new List<CartLineItemView>();

            foreach (var item in cart.Items)
            {
                var product = await catalogClient.GetProductAsync(item.Sku, ct);

                enrichedItems.Add(new CartLineItemView(
                    item.Sku,
                    product?.Name ?? "Unknown Product",
                    product?.Images.FirstOrDefault()?.Url ?? "",
                    item.Quantity,
                    item.UnitPrice,
                    item.Quantity * item.UnitPrice));
            }

            var subtotal = enrichedItems.Sum(i => i.LineTotal);

            var cartView = new CartView(
                cart.Id,
                cart.CustomerId,
                enrichedItems,
                subtotal);

            return Results.Ok(cartView);
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return Results.NotFound();
            }
            throw;
        }
    }
}
