using Microsoft.AspNetCore.Http;
using Storefront.Clients;
using Storefront.Composition;
using Wolverine.Http;

namespace Storefront.Api.Queries;

/// <summary>
/// Query to get composed checkout view (Orders BC + Customer Identity BC)
/// </summary>
public sealed record GetCheckoutView(Guid CheckoutId);

public static class GetCheckoutViewHandler
{
    [WolverineGet("/api/storefront/checkouts/{checkoutId}")]
    public static async Task<IResult> Handle(
        Guid checkoutId,
        IOrdersClient ordersClient,
        ICustomerIdentityClient identityClient,
        ICatalogClient catalogClient,
        CancellationToken ct)
    {
        try
        {
            // Query Orders BC for checkout state
            var checkout = await ordersClient.GetCheckoutAsync(checkoutId, ct);

            // Query Customer Identity BC for saved addresses
            var addresses = await identityClient.GetCustomerAddressesAsync(
                checkout.CustomerId,
                "Shipping", // Filter to shipping addresses only
                ct);

            // Enrich line items with product details from Catalog BC
            var enrichedItems = new List<CartLineItemView>();

            foreach (var item in checkout.Items)
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
            var shippingCost = 5.99m; // Hardcoded for now (future: calculate based on shipping method)
            var total = subtotal + shippingCost;

            // Map addresses to AddressSummary
            var addressSummaries = addresses.Select(addr => new AddressSummary(
                addr.Id,
                addr.Nickname,
                $"{addr.AddressLine1}, {addr.City}, {addr.StateOrProvince} {addr.PostalCode}"
            )).ToList();

            var checkoutView = new CheckoutView(
                checkout.Id,
                checkout.CustomerId,
                CheckoutStep.ShippingAddress, // TODO: Track actual step in Orders BC
                enrichedItems,
                addressSummaries,
                subtotal,
                shippingCost,
                total,
                CanProceedToNextStep: true); // TODO: Implement validation logic

            return Results.Ok(checkoutView);
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
