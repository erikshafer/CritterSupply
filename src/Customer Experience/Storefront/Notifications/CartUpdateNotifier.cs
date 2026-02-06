using Storefront.Clients;

namespace Storefront.Notifications;

/// <summary>
/// Handles cart-related integration messages from Shopping BC and pushes SSE updates to connected clients.
/// </summary>
public static class CartUpdateNotifier
{
    /// <summary>
    /// Item added to cart → query Shopping BC → broadcast SSE update
    /// </summary>
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

    /// <summary>
    /// Item removed from cart → query Shopping BC → broadcast SSE update
    /// </summary>
    public static async Task Handle(
        Messages.Contracts.Shopping.ItemRemoved message,
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
            message.RemovedAt);

        // Broadcast to all connected clients for this customer
        await broadcaster.BroadcastAsync(message.CustomerId, @event, ct);
    }

    /// <summary>
    /// Item quantity changed → query Shopping BC → broadcast SSE update
    /// </summary>
    public static async Task Handle(
        Messages.Contracts.Shopping.ItemQuantityChanged message,
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
            message.ChangedAt);

        // Broadcast to all connected clients for this customer
        await broadcaster.BroadcastAsync(message.CustomerId, @event, ct);
    }
}
