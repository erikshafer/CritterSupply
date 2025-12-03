using Wolverine;

namespace Orders.Placement;

/// <summary>
/// Wolverine saga for coordinating the order lifecycle across bounded contexts.
/// Sagas are identified by their Id property and persisted by Marten.
/// </summary>
public sealed class Order : Saga
{
    /// <summary>
    /// Saga identity - used as correlation ID for all related messages.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The customer who placed the order.
    /// </summary>
    public Guid CustomerId { get; set; }

    /// <summary>
    /// The line items in the order.
    /// </summary>
    public IReadOnlyList<OrderLineItem> LineItems { get; set; } = [];

    /// <summary>
    /// The delivery destination for the order.
    /// </summary>
    public ShippingAddress ShippingAddress { get; set; } = null!;

    /// <summary>
    /// The selected shipping method.
    /// </summary>
    public string ShippingMethod { get; set; } = string.Empty;

    /// <summary>
    /// Reference to the customer's selected payment method.
    /// </summary>
    public string PaymentMethodToken { get; set; } = string.Empty;

    /// <summary>
    /// The total amount for the order.
    /// </summary>
    public decimal TotalAmount { get; set; }

    /// <summary>
    /// The current status of the order saga.
    /// </summary>
    public OrderStatus Status { get; set; }

    /// <summary>
    /// When the order was placed.
    /// </summary>
    public DateTimeOffset PlacedAt { get; set; }

    /// <summary>
    /// Saga start handler - creates the saga from CheckoutCompleted.
    /// Validation happens in Wolverine middleware via FluentValidation.
    /// </summary>
    /// <param name="command">The checkout completed event from Shopping context.</param>
    /// <returns>A tuple of the new Order saga and the OrderPlaced event to publish.</returns>
    public static (Order, OrderPlaced) Start(CheckoutCompleted command)
    {
        var orderId = Guid.CreateVersion7();
        var placedAt = DateTimeOffset.UtcNow;

        var lineItems = command.LineItems
            .Select(item => new OrderLineItem(
                item.Sku,
                item.Quantity,
                item.PriceAtPurchase,
                item.Quantity * item.PriceAtPurchase))
            .ToList();

        var totalAmount = lineItems.Sum(x => x.LineTotal);

        var saga = new Order
        {
            Id = orderId,
            CustomerId = command.CustomerId,
            LineItems = lineItems,
            ShippingAddress = command.ShippingAddress,
            ShippingMethod = command.ShippingMethod,
            PaymentMethodToken = command.PaymentMethodToken,
            TotalAmount = totalAmount,
            Status = OrderStatus.Placed,
            PlacedAt = placedAt
        };

        var @event = new OrderPlaced(
            orderId,
            command.CustomerId,
            lineItems,
            command.ShippingAddress,
            command.ShippingMethod,
            command.PaymentMethodToken,
            totalAmount,
            placedAt);

        return (saga, @event);
    }

    // Future saga handlers for other events will be added here:
    // Handle(PaymentCaptured) -> transition to PaymentConfirmed
    // Handle(PaymentFailed) -> transition to PaymentFailed or retry
    // Handle(ReservationCommitted) -> proceed to fulfillment
    // etc.
}
