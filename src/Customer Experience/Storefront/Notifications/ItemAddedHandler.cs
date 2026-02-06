using Storefront.Clients;

namespace Storefront.Notifications;

/// <summary>
/// Handles Shopping.ItemAdded integration message and broadcasts SSE update to connected clients.
/// </summary>
public static class ItemAddedHandler
{
    public static async Task Handle(
        Messages.Contracts.Shopping.ItemAdded message,
        IShoppingClient shoppingClient,
        IEventBroadcaster broadcaster,
        CancellationToken ct)
    {
        // Query Shopping BC for updated cart state
        var cart = await shoppingClient.GetCartAsync(message.CartId, ct);

        if (cart is null)
            return; // Cart not found (race condition or deleted)

        // Calculate total from items
        var totalAmount = cart.Items.Sum(item => item.Quantity * item.UnitPrice);

        // Compose SSE event
        var @event = new CartUpdated(
            cart.Id,
            message.CustomerId,
            cart.Items.Count,
            totalAmount,
            message.AddedAt);

        // Broadcast to all connected clients for this customer
        await broadcaster.BroadcastAsync(message.CustomerId, @event, ct);
    }
}
