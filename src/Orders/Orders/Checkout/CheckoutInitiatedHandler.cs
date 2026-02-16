using Marten;
using Wolverine;
using ShoppingContracts = Messages.Contracts.Shopping;

namespace Orders.Checkout;

/// <summary>
/// Integration handler that receives CheckoutInitiated from Shopping BC
/// and starts the Checkout aggregate in Orders BC.
/// </summary>
public static class CheckoutInitiatedHandler
{
    public static void Handle(
        ShoppingContracts.CheckoutInitiated message,
        IDocumentSession session)
    {
        var startedEvent = new CheckoutStarted(
            message.CheckoutId,
            message.CartId,
            message.CustomerId,
            message.Items,
            message.InitiatedAt);

        session.Events.StartStream<Checkout>(message.CheckoutId, startedEvent);
    }
}
