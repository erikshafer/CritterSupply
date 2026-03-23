using IntegrationMessages = Messages.Contracts.Orders;

namespace Orders.Placement;

/// <summary>
/// Message handler that receives CartCheckoutCompleted integration message and starts the Order saga.
/// Separated from the Order saga class to keep state-transition handlers focused.
/// The return type of (Order, ...) is recognized by Wolverine as a saga start handler.
/// </summary>
public static class PlaceOrderHandler
{
    /// <summary>
    /// Handles CartCheckoutCompleted integration message by starting a new Order saga.
    /// This is the ONLY way to create Order sagas in production.
    /// </summary>
    /// <param name="message">The cart checkout completed integration message from Shopping BC.</param>
    /// <returns>The new Order saga instance + OrderPlaced cascading message.</returns>
    public static (Order, IntegrationMessages.OrderPlaced) Handle(
        Messages.Contracts.Shopping.CartCheckoutCompleted message)
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
        return OrderDecider.Start(command, DateTimeOffset.UtcNow);
    }
}
