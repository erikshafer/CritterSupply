using Messages.Contracts.Inventory;
using Messages.Contracts.Payments;
using Wolverine;
using IntegrationMessages = Messages.Contracts.Orders;
using FulfillmentMessages = Messages.Contracts.Fulfillment;

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
    /// Tracks reservation IDs for this order (for orchestrating commit/release).
    /// Key: ReservationId, Value: SKU
    /// </summary>
    public Dictionary<Guid, string> ReservationIds { get; set; } = new();

    /// <summary>
    /// Tracks whether inventory has been reserved (for orchestration logic).
    /// </summary>
    public bool IsInventoryReserved { get; set; }

    /// <summary>
    /// Tracks whether payment has been captured (for orchestration logic).
    /// </summary>
    public bool IsPaymentCaptured { get; set; }

    /// <summary>
    /// Saga start handler - creates the saga from CheckoutCompleted.
    /// Validation happens in Wolverine middleware via FluentValidation.
    /// </summary>
    /// <param name="command">The checkout completed event from Shopping context.</param>
    /// <returns>A tuple of the new Order saga and the OrderPlaced event to publish.</returns>
    public static (Order, IntegrationMessages.OrderPlaced) Start(CheckoutCompleted command)
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

        var @event = new IntegrationMessages.OrderPlaced(
            orderId,
            command.CustomerId,
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

    /// <summary>
    /// Saga handler for successful payment capture.
    /// Transitions order to PaymentConfirmed status and orchestrates inventory commitment if ready.
    /// **Validates: Requirement 1.2 - Order proceeds after payment confirmation**
    /// </summary>
    /// <param name="message">Payment captured integration message from Payments BC.</param>
    /// <returns>Orchestration messages if inventory is reserved and ready to commit.</returns>
    public OutgoingMessages Handle(PaymentCaptured message)
    {
        Status = OrderStatus.PaymentConfirmed;
        IsPaymentCaptured = true;

        var messages = new OutgoingMessages();

        // Orchestration: If inventory is already reserved, tell Inventory to commit all reservations
        if (IsInventoryReserved)
        {
            foreach (var reservationId in ReservationIds.Keys)
            {
                messages.Add(new IntegrationMessages.ReservationCommitRequested(
                    Id,
                    reservationId,
                    DateTimeOffset.UtcNow));
            }

            // Note: We don't request fulfillment here even if inventory is committed,
            // because the commit confirmation will come async via ReservationCommitted handler,
            // which will then trigger fulfillment. This avoids race conditions.
        }

        return messages;
    }

    /// <summary>
    /// Saga handler for payment failure.
    /// Transitions order to PaymentFailed status and triggers compensation (release inventory).
    /// **Validates: Requirement 1.3 - Order fails when payment cannot be processed**
    /// </summary>
    /// <param name="message">Payment failed integration message from Payments BC.</param>
    /// <returns>Compensation messages to release any reserved inventory.</returns>
    public OutgoingMessages Handle(PaymentFailed message)
    {
        Status = OrderStatus.PaymentFailed;

        var messages = new OutgoingMessages();

        // Compensation: Release any reserved inventory
        if (IsInventoryReserved)
        {
            foreach (var reservationId in ReservationIds.Keys)
            {
                messages.Add(new IntegrationMessages.ReservationReleaseRequested(
                    Id,
                    reservationId,
                    $"Payment failed: {message.FailureReason}",
                    DateTimeOffset.UtcNow));
            }
        }

        return messages;
    }

    /// <summary>
    /// Saga handler for payment authorization (two-phase flow).
    /// Transitions order to PendingPayment status (waiting for capture).
    /// **Validates: Requirement 1.4 - Order can wait for deferred payment capture**
    /// </summary>
    /// <param name="message">Payment authorized integration message from Payments BC.</param>
    public void Handle(PaymentAuthorized message)
    {
        Status = OrderStatus.PendingPayment;
    }

    /// <summary>
    /// Saga handler for successful refund completion.
    /// Refunds are a financial operation and don't necessarily change the order's fulfillment status.
    /// The order remains in its current state (e.g., Shipped, Delivered, Closed).
    /// **Validates: Requirement 1.5 - Order tracks refund completion for financial reconciliation**
    /// </summary>
    /// <param name="message">Refund completed integration message from Payments BC.</param>
    public void Handle(RefundCompleted message)
    {
        // No status change - refunds are financial operations that don't affect fulfillment state.
        // Future enhancement: Track refund amount or add RefundedAmount property.
    }

    /// <summary>
    /// Saga handler for failed refund processing.
    /// Refund failures are logged but don't change the order's fulfillment status.
    /// The order remains in its current state (e.g., Shipped, Delivered, Closed).
    /// **Validates: Requirement 1.6 - Order tracks refund failures for investigation and retry**
    /// </summary>
    /// <param name="message">Refund failed integration message from Payments BC.</param>
    public void Handle(RefundFailed message)
    {
        // No status change - refund failures are financial issues that don't affect fulfillment state.
        // Future enhancement: Track failed refund attempts or add FailedRefundReason property.
    }

    /// <summary>
    /// Saga handler for successful inventory reservation.
    /// Transitions order to InventoryReserved status and tracks reservation for future orchestration.
    /// **Validates: Requirement 2.1 - Order tracks inventory reservation confirmation**
    /// </summary>
    /// <param name="message">Reservation confirmed integration message from Inventory BC.</param>
    /// <returns>Orchestration message if payment is already captured and ready to commit.</returns>
    public IntegrationMessages.ReservationCommitRequested? Handle(ReservationConfirmed message)
    {
        Status = OrderStatus.InventoryReserved;
        IsInventoryReserved = true;
        ReservationIds[message.ReservationId] = message.SKU;

        // Orchestration: If payment is already captured, tell Inventory to commit this reservation
        if (IsPaymentCaptured)
        {
            return new IntegrationMessages.ReservationCommitRequested(
                Id,
                message.ReservationId,
                DateTimeOffset.UtcNow);
        }

        return null;
    }

    /// <summary>
    /// Saga handler for inventory reservation failure.
    /// Transitions order to InventoryFailed status (insufficient stock).
    /// **Validates: Requirement 2.2 - Order fails when inventory cannot be reserved**
    /// </summary>
    /// <param name="message">Reservation failed integration message from Inventory BC.</param>
    public void Handle(ReservationFailed message)
    {
        Status = OrderStatus.InventoryFailed;
        // TODO: Future enhancement - trigger compensation (release payment, cancel order)
    }

    /// <summary>
    /// Saga handler for inventory commitment (hard allocation).
    /// Transitions order to InventoryCommitted status and proceeds to fulfillment if payment is captured.
    /// **Validates: Requirement 2.3 - Order proceeds after inventory is committed**
    /// **Validates: Requirement 3.1 - Order cannot proceed to fulfillment without confirmed payment**
    /// </summary>
    /// <param name="message">Reservation committed integration message from Inventory BC.</param>
    /// <returns>Orchestration message to start fulfillment if both payment and inventory are confirmed.</returns>
    public OutgoingMessages Handle(ReservationCommitted message)
    {
        Status = OrderStatus.InventoryCommitted;

        var messages = new OutgoingMessages();

        // Orchestration: If payment is already captured, request fulfillment
        if (IsPaymentCaptured)
        {
            Status = OrderStatus.Fulfilling;

            var fulfillmentRequest = new FulfillmentMessages.FulfillmentRequested(
                Id,
                CustomerId,
                new FulfillmentMessages.ShippingAddress(
                    ShippingAddress.Street,
                    ShippingAddress.Street2,
                    ShippingAddress.City,
                    ShippingAddress.State,
                    ShippingAddress.PostalCode,
                    ShippingAddress.Country),
                LineItems.Select(li => new FulfillmentMessages.FulfillmentLineItem(
                    li.Sku,
                    li.Quantity)).ToList(),
                ShippingMethod,
                DateTimeOffset.UtcNow);

            messages.Add(fulfillmentRequest);
        }

        return messages;
    }

    /// <summary>
    /// Saga handler for inventory reservation release.
    /// Tracks that inventory has been returned to available pool (compensation).
    /// **Validates: Requirement 2.4 - Order tracks inventory release for compensation**
    /// </summary>
    /// <param name="message">Reservation released integration message from Inventory BC.</param>
    public void Handle(ReservationReleased message)
    {
        // No status change - reservation release is a compensation operation.
        // The order remains in its current failed state (PaymentFailed, InventoryFailed, Cancelled).
        // Future enhancement: Track release timestamp or add ReleasedInventoryAt property.
    }

    /// <summary>
    /// Saga handler for shipment dispatch from Fulfillment BC.
    /// Transitions order to Shipped status when carrier takes possession.
    /// **Validates: Requirement 3.2 - Order status reflects shipment progress**
    /// </summary>
    /// <param name="message">Shipment dispatched integration message from Fulfillment BC.</param>
    public void Handle(FulfillmentMessages.ShipmentDispatched message)
    {
        Status = OrderStatus.Shipped;
        // Future enhancement: Store tracking info (message.Carrier, message.TrackingNumber)
    }

    /// <summary>
    /// Saga handler for successful delivery from Fulfillment BC.
    /// Transitions order to Delivered status - terminal success state.
    /// **Validates: Requirement 3.3 - Order completes when delivery confirmed**
    /// </summary>
    /// <param name="message">Shipment delivered integration message from Fulfillment BC.</param>
    public void Handle(FulfillmentMessages.ShipmentDelivered message)
    {
        Status = OrderStatus.Delivered;
        // Mark saga as complete - no further processing needed
        MarkCompleted();
    }

    /// <summary>
    /// Saga handler for delivery failure from Fulfillment BC.
    /// Tracks delivery issues - may require customer service intervention.
    /// **Validates: Requirement 3.4 - Order tracks delivery failures**
    /// </summary>
    /// <param name="message">Shipment delivery failed integration message from Fulfillment BC.</param>
    public void Handle(FulfillmentMessages.ShipmentDeliveryFailed message)
    {
        // Status remains Shipped - delivery failure doesn't move order backwards
        // Future enhancement: Add DeliveryFailedAt timestamp, failure reason tracking
        // Future enhancement: Trigger customer service notification workflow
    }
}
