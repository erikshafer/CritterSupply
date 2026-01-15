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
    /// Key: ReservationId, Value: Sku
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
    /// Saga start handler - creates the saga from CheckoutCompleted integration message.
    /// This is the ONLY way to start an Order saga in production.
    /// Maps Shopping BC's CheckoutCompleted to Orders domain's CheckoutCompleted command.
    /// Wolverine convention: static Start() method on saga class.
    /// </summary>
    /// <param name="message">The checkout completed integration message from Shopping BC.</param>
    /// <returns>A tuple of the new Order saga and the OrderPlaced event to publish.</returns>
    public static (Order, IntegrationMessages.OrderPlaced) Start(Messages.Contracts.Shopping.CheckoutCompleted message)
    {
        // Map integration message to local command
        var command = new CheckoutCompleted(
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

        // Delegate to pure Decider function (pass current timestamp)
        return OrderDecider.Start(command, DateTimeOffset.UtcNow);
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
        var decision = OrderDecider.HandlePaymentCaptured(this, message, DateTimeOffset.UtcNow);

        // Apply state changes from decision
        if (decision.Status.HasValue) Status = decision.Status.Value;
        if (decision.IsPaymentCaptured.HasValue) IsPaymentCaptured = decision.IsPaymentCaptured.Value;

        // Return outgoing messages
        var outgoing = new OutgoingMessages();
        foreach (var msg in decision.Messages) outgoing.Add(msg);
        return outgoing;
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
        var decision = OrderDecider.HandlePaymentFailed(this, message, DateTimeOffset.UtcNow);

        // Apply state changes from decision
        if (decision.Status.HasValue) Status = decision.Status.Value;

        // Return outgoing messages
        var outgoing = new OutgoingMessages();
        foreach (var msg in decision.Messages) outgoing.Add(msg);
        return outgoing;
    }

    /// <summary>
    /// Saga handler for payment authorization (two-phase flow).
    /// Transitions order to PendingPayment status (waiting for capture).
    /// **Validates: Requirement 1.4 - Order can wait for deferred payment capture**
    /// </summary>
    /// <param name="message">Payment authorized integration message from Payments BC.</param>
    public void Handle(PaymentAuthorized message)
    {
        var decision = OrderDecider.HandlePaymentAuthorized(this, message);

        // Apply state changes from decision
        if (decision.Status.HasValue) Status = decision.Status.Value;
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
        var decision = OrderDecider.HandleRefundCompleted(this, message);

        // Apply state changes from decision (none expected for refunds)
        if (decision.Status.HasValue) Status = decision.Status.Value;
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
        var decision = OrderDecider.HandleRefundFailed(this, message);

        // Apply state changes from decision (none expected for refund failures)
        if (decision.Status.HasValue) Status = decision.Status.Value;
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
        var decision = OrderDecider.HandleReservationConfirmed(this, message, DateTimeOffset.UtcNow);

        // Apply state changes from decision
        if (decision.Status.HasValue) Status = decision.Status.Value;
        if (decision.IsInventoryReserved.HasValue) IsInventoryReserved = decision.IsInventoryReserved.Value;
        if (decision.ReservationIds != null) ReservationIds = decision.ReservationIds;

        // Return single orchestration message (if any)
        return decision.Messages.FirstOrDefault() as IntegrationMessages.ReservationCommitRequested;
    }

    /// <summary>
    /// Saga handler for inventory reservation failure.
    /// Transitions order to InventoryFailed status (insufficient stock).
    /// **Validates: Requirement 2.2 - Order fails when inventory cannot be reserved**
    /// </summary>
    /// <param name="message">Reservation failed integration message from Inventory BC.</param>
    public void Handle(ReservationFailed message)
    {
        var decision = OrderDecider.HandleReservationFailed(this, message);

        // Apply state changes from decision
        if (decision.Status.HasValue) Status = decision.Status.Value;
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
        var decision = OrderDecider.HandleReservationCommitted(this, message, DateTimeOffset.UtcNow);

        // Apply state changes from decision
        if (decision.Status.HasValue) Status = decision.Status.Value;

        // Return outgoing messages
        var outgoing = new OutgoingMessages();
        foreach (var msg in decision.Messages) outgoing.Add(msg);
        return outgoing;
    }

    /// <summary>
    /// Saga handler for inventory reservation release.
    /// Tracks that inventory has been returned to available pool (compensation).
    /// **Validates: Requirement 2.4 - Order tracks inventory release for compensation**
    /// </summary>
    /// <param name="message">Reservation released integration message from Inventory BC.</param>
    public void Handle(ReservationReleased message)
    {
        var decision = OrderDecider.HandleReservationReleased(this, message);

        // Apply state changes from decision (none expected for compensation)
        if (decision.Status.HasValue) Status = decision.Status.Value;
    }

    /// <summary>
    /// Saga handler for shipment dispatch from Fulfillment BC.
    /// Transitions order to Shipped status when carrier takes possession.
    /// **Validates: Requirement 3.2 - Order status reflects shipment progress**
    /// </summary>
    /// <param name="message">Shipment dispatched integration message from Fulfillment BC.</param>
    public void Handle(FulfillmentMessages.ShipmentDispatched message)
    {
        var decision = OrderDecider.HandleShipmentDispatched(this, message);

        // Apply state changes from decision
        if (decision.Status.HasValue) Status = decision.Status.Value;
    }

    /// <summary>
    /// Saga handler for successful delivery from Fulfillment BC.
    /// Transitions order to Delivered status - terminal success state.
    /// **Validates: Requirement 3.3 - Order completes when delivery confirmed**
    /// </summary>
    /// <param name="message">Shipment delivered integration message from Fulfillment BC.</param>
    public void Handle(FulfillmentMessages.ShipmentDelivered message)
    {
        var decision = OrderDecider.HandleShipmentDelivered(this, message);

        // Apply state changes from decision
        if (decision.Status.HasValue) Status = decision.Status.Value;

        // Mark saga as complete if decider signals completion
        if (decision.ShouldComplete) MarkCompleted();
    }

    /// <summary>
    /// Saga handler for delivery failure from Fulfillment BC.
    /// Tracks delivery issues - may require customer service intervention.
    /// **Validates: Requirement 3.4 - Order tracks delivery failures**
    /// </summary>
    /// <param name="message">Shipment delivery failed integration message from Fulfillment BC.</param>
    public void Handle(FulfillmentMessages.ShipmentDeliveryFailed message)
    {
        var decision = OrderDecider.HandleShipmentDeliveryFailed(this, message);

        // Apply state changes from decision (none expected - remains Shipped)
        if (decision.Status.HasValue) Status = decision.Status.Value;
    }
}
