using Storefront.Clients;
using Storefront.RealTime;
using Wolverine.SignalR;

namespace Storefront.Notifications;

/// <summary>
/// Handles Shopping.ItemAdded integration message and publishes SignalR update via Wolverine.
/// Scoped to the authenticated customer's group to prevent cross-customer event leakage.
/// </summary>
public static class ItemAddedHandler
{
    public static async Task<SignalRMessage<CartUpdated>?> Handle(
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

        var cartUpdated = new CartUpdated(
            cart.Id,
            message.CustomerId,
            cart.Items.Count,
            totalAmount,
            message.AddedAt);

        // Send only to the authenticated customer's group — not broadcast to all clients
        return cartUpdated.ToWebSocketGroup($"customer:{message.CustomerId}");
    }
}
