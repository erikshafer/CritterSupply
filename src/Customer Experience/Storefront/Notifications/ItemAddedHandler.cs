using Storefront.Clients;
using Storefront.RealTime;

namespace Storefront.Notifications;

/// <summary>
/// Handles Shopping.ItemAdded integration message and publishes SignalR update via Wolverine.
/// </summary>
public static class ItemAddedHandler
{
    public static async Task<CartUpdated?> Handle(
        Messages.Contracts.Shopping.ItemAdded message,
        IShoppingClient shoppingClient,
        CancellationToken ct)
    {
        // Query Shopping BC for updated cart state
        var cart = await shoppingClient.GetCartAsync(message.CartId, ct);

        if (cart is null)
            return null; // Cart not found (race condition or deleted)

        // Calculate total from items
        var totalAmount = cart.Items.Sum(item => item.Quantity * item.UnitPrice);

        // Return SignalR message — Wolverine routes to hub based on IStorefrontWebSocketMessage
        return new CartUpdated(
            cart.Id,
            message.CustomerId,
            cart.Items.Count,
            totalAmount,
            message.AddedAt);
    }
}
