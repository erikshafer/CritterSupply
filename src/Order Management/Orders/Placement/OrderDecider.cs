using IntegrationMessages = Messages.Contracts.Orders;

namespace Orders.Placement;

/// <summary>
/// Pure functions implementing the Decider pattern for Order saga.
/// Contains all business logic for order lifecycle decisions.
/// These functions have no side effects and are easily testable.
/// </summary>
public static class OrderDecider
{
    /// <summary>
    /// Decides to create a new Order saga from CheckoutCompleted.
    /// Pure function - no side effects, just business logic.
    /// </summary>
    /// <param name="command">The checkout completed event from Shopping context.</param>
    /// <returns>A tuple of the new Order saga and the OrderPlaced event to publish.</returns>
    public static (Order, IntegrationMessages.OrderPlaced) Start(CheckoutCompleted command)
    {
        var orderId = command.OrderId;
        var placedAt = DateTimeOffset.UtcNow;

        var lineItems = command.LineItems
            .Select(item => new OrderLineItem(
                item.Sku,
                item.Quantity,
                item.PriceAtPurchase,
                item.Quantity * item.PriceAtPurchase))
            .ToList();

        var subtotal = lineItems.Sum(x => x.LineTotal);
        var totalAmount = subtotal + command.ShippingCost;

        // Create the saga instance (write model)
        var saga = new Order
        {
            Id = orderId,
            CustomerId = command.CustomerId!.Value, // Validator ensures non-null
            LineItems = lineItems,
            ShippingAddress = command.ShippingAddress,
            ShippingMethod = command.ShippingMethod,
            PaymentMethodToken = command.PaymentMethodToken,
            TotalAmount = totalAmount,
            Status = OrderStatus.Placed,
            PlacedAt = placedAt
        };

        // Create the integration event to publish
        var orderPlaced = new IntegrationMessages.OrderPlaced(
            orderId,
            command.CustomerId!.Value,
            lineItems.Select(li => new IntegrationMessages.OrderLineItem(
                li.Sku,
                li.Quantity,
                li.UnitPrice,
                li.LineTotal)).ToList(),
            new IntegrationMessages.ShippingAddress(
                command.ShippingAddress.Street,
                command.ShippingAddress.Street2,
                command.ShippingAddress.City,
                command.ShippingAddress.State,
                command.ShippingAddress.PostalCode,
                command.ShippingAddress.Country),
            command.ShippingMethod,
            command.PaymentMethodToken,
            totalAmount,
            placedAt);

        return (saga, orderPlaced);
    }
}
