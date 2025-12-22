using IntegrationMessages = Messages.Contracts.Orders;

namespace Orders.Placement;

/// <summary>
/// Integration event received from Shopping context when checkout completes.
/// This message starts the Order saga.
/// Maps from Messages.Contracts.Shopping.CheckoutCompleted.
/// </summary>
public sealed record CheckoutCompleted(
    Guid OrderId,
    Guid CheckoutId,
    Guid? CustomerId,
    IReadOnlyList<CheckoutLineItem> LineItems,
    ShippingAddress ShippingAddress,
    string ShippingMethod,
    decimal ShippingCost,
    string PaymentMethodToken,
    DateTimeOffset CompletedAt);

/// <summary>
/// Handler that starts the Order saga from CheckoutCompleted message.
/// </summary>
public static class CheckoutCompletedHandler
{
    /// <summary>
    /// Saga start handler - creates the saga from CheckoutCompleted.
    /// Validation happens in Wolverine middleware via FluentValidation.
    /// </summary>
    /// <param name="command">The checkout completed event from Shopping context.</param>
    /// <returns>A tuple of the new Order saga and the OrderPlaced event to publish.</returns>
    public static (Order, IntegrationMessages.OrderPlaced) Handle(CheckoutCompleted command)
    {
        var orderId = command.OrderId; // Use OrderId from Shopping BC
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

        var @event = new IntegrationMessages.OrderPlaced(
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

        return (saga, @event);
    }
}
