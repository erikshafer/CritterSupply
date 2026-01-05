using ShoppingContracts = Messages.Contracts.Shopping;
using IntegrationMessages = Messages.Contracts.Orders;

namespace Orders.Placement;

/// <summary>
/// Handler for integration messages from Shopping bounded context.
/// Maps external integration contracts to internal saga commands and starts the Order saga.
/// </summary>
public static class ShoppingIntegrationHandler
{
    /// <summary>
    /// Receives CheckoutCompleted from Shopping BC, maps it to local command, and starts Order saga.
    /// Returns the saga for persistence and OrderPlaced message for publication.
    /// </summary>
    /// <param name="message">Integration message from Shopping BC.</param>
    /// <returns>Tuple of Order saga and OrderPlaced event.</returns>
    public static (Order, IntegrationMessages.OrderPlaced) Handle(ShoppingContracts.CheckoutCompleted message)
    {
        // Map Shopping's CheckoutLineItem to Orders' CheckoutLineItem
        var lineItems = message.Items
            .Select(item => new CheckoutLineItem(
                item.Sku,
                item.Quantity,
                item.UnitPrice))
            .ToList();

        // Map Shopping's ShippingAddress to Orders' ShippingAddress
        var shippingAddress = new ShippingAddress(
            message.ShippingAddress.AddressLine1,
            message.ShippingAddress.AddressLine2,
            message.ShippingAddress.City,
            message.ShippingAddress.StateOrProvince,
            message.ShippingAddress.PostalCode,
            message.ShippingAddress.Country);

        // Create local CheckoutCompleted command
        var checkoutCompleted = new CheckoutCompleted(
            message.OrderId,
            message.CheckoutId,
            message.CustomerId,
            lineItems,
            shippingAddress,
            message.ShippingMethod,
            message.ShippingCost,
            message.PaymentMethodToken,
            message.CompletedAt);

        // Start the Order saga (delegates to OrderDecider)
        return Order.Start(checkoutCompleted);
    }
}
