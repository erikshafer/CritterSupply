using Wolverine.Marten;
using IntegrationMessages = Messages.Contracts.Orders;

namespace Orders.Placement;

/// <summary>
/// Message handler that receives CheckoutCompleted integration message and starts the Order saga.
/// Uses IStartStream pattern for idiomatic Wolverine saga initialization.
/// </summary>
public static class PlaceOrderHandler
{
    /// <summary>
    /// Handles CheckoutCompleted integration message by starting a new Order saga.
    /// This is the ONLY way to create Order sagas in production.
    /// </summary>
    /// <param name="message">The checkout completed integration message (internal to Orders BC).</param>
    /// <returns>Stream start operation for the Order saga + OrderPlaced cascading message.</returns>
    public static (IStartStream, IntegrationMessages.OrderPlaced) Handle(
        Messages.Contracts.Shopping.CheckoutCompleted message)
    {
        // Map integration message to domain command
        var command = new PlaceOrder(
            message.OrderId,
            message.CheckoutId,
            message.CustomerId,
            message.Items.Select(i => new CheckoutLineItem(i.Sku, i.Quantity, i.UnitPrice)).ToList(),
            new ShippingAddress(
                message.ShippingAddress.AddressLine1,
                message.ShippingAddress.AddressLine2,
                message.ShippingAddress.City,
                message.ShippingAddress.StateOrProvince,
                message.ShippingAddress.PostalCode,
                message.ShippingAddress.Country),
            message.ShippingMethod,
            message.ShippingCost,
            message.PaymentMethodToken,
            message.CompletedAt);

        // Delegate to pure Decider function for business logic
        var (saga, orderPlaced) = OrderDecider.Start(command, DateTimeOffset.UtcNow);

        // Start saga stream using idiomatic Wolverine pattern
        var stream = MartenOps.StartStream<Order>(saga.Id, saga);

        // Return stream start + cascading message (both processed by Wolverine)
        return (stream, orderPlaced);
    }
}
