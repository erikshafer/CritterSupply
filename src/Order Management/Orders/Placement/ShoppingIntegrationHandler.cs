using ShoppingContracts = Messages.Contracts.Shopping;

namespace Orders.Placement;

/// <summary>
/// Handler for integration messages from Shopping bounded context.
/// Maps external integration contracts to internal saga commands.
/// </summary>
public static class ShoppingIntegrationHandler
{
    /// <summary>
    /// Receives CheckoutCompleted from Shopping BC and maps to local CheckoutCompleted for saga creation.
    /// </summary>
    /// <param name="message">Integration message from Shopping BC.</param>
    /// <returns>Local CheckoutCompleted command that will start the Order saga.</returns>
    public static CheckoutCompleted Handle(ShoppingContracts.CheckoutCompleted message)
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

        return new CheckoutCompleted(
            message.OrderId,
            message.CheckoutId,
            message.CustomerId,
            lineItems,
            shippingAddress,
            message.ShippingMethod,
            message.ShippingCost,
            message.PaymentMethodToken,
            message.CompletedAt);
    }
}
