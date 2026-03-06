using Wolverine.Marten;
using ShoppingContracts = Messages.Contracts.Shopping;

namespace Orders.Checkout;

/// <summary>
/// Integration handler that receives CheckoutInitiated from Shopping BC
/// and starts the Checkout aggregate in Orders BC.
/// Uses IStartStream pattern - Wolverine automatically handles persistence.
/// </summary>
public static class CheckoutInitiatedHandler
{
    /// <summary>
    /// Handles CheckoutInitiated integration message from Shopping BC.
    /// Returns IStartStream - Wolverine enrolls in transactional outbox and calls SaveChangesAsync() automatically.
    /// </summary>
    public static IStartStream Handle(ShoppingContracts.CheckoutInitiated message)
    {
        var startedEvent = new CheckoutStarted(
            message.CheckoutId,
            message.CartId,
            message.CustomerId,
            message.Items,
            message.InitiatedAt);

        // MartenOps.StartStream returns IStartStream
        // Wolverine detects this return type and:
        // 1. Injects IDocumentSession
        // 2. Enrolls in transactional outbox
        // 3. Calls SaveChangesAsync() after handler completes
        return MartenOps.StartStream<Checkout>(message.CheckoutId, startedEvent);
    }
}
