using Messages.Contracts.Inventory;
using Messages.Contracts.Payments;
using Wolverine;
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
    /// Number of distinct SKUs expected to produce inventory reservations.
    /// Set at saga start to enable multi-SKU reservation tracking.
    /// </summary>
    public int ExpectedReservationCount { get; set; }

    /// <summary>
    /// Number of individual SKU reservations that have been confirmed by Inventory BC.
    /// When this equals ExpectedReservationCount, all inventory is reserved.
    /// </summary>
    public int ConfirmedReservationCount { get; set; }

    /// <summary>
    /// Number of individual SKU reservations that have been committed (hard-allocated) by Inventory BC.
    /// Derived from CommittedReservationIds to keep a single source of truth and prevent drift.
    /// When this equals ExpectedReservationCount and payment is captured, fulfillment can be requested.
    /// </summary>
    public int CommittedReservationCount => CommittedReservationIds.Count;

    /// <summary>
    /// Tracks which reservation IDs have already been committed (hard-allocated).
    /// Used as an idempotency guard to prevent double-counting duplicate ReservationCommitted messages
    /// under at-least-once delivery.
    /// </summary>
    public HashSet<Guid> CommittedReservationIds { get; set; } = [];

    /// <summary>
    /// True when all expected SKU reservations have been confirmed by Inventory BC.
    /// Derived from reservation counts; not stored separately.
    /// </summary>
    public bool IsInventoryReserved => ExpectedReservationCount > 0
        && ConfirmedReservationCount >= ExpectedReservationCount;

    /// <summary>
    /// True when all confirmed reservations have been committed (hard-allocated) by Inventory BC.
    /// </summary>
    public bool IsAllInventoryCommitted => ExpectedReservationCount > 0
        && CommittedReservationCount >= ExpectedReservationCount;

    /// <summary>
    /// Tracks whether payment has been captured (for orchestration logic).
    /// </summary>
    public bool IsPaymentCaptured { get; set; }

    /// <summary>
    /// The payment ID captured for this order. Used when issuing a refund for returns.
    /// </summary>
    public Guid? PaymentId { get; set; }

    /// <summary>
    /// List of active return IDs currently in progress.
    /// Prevents premature saga closure when ReturnWindowExpired fires while returns are being processed.
    /// Supports multiple concurrent returns (sequential returns from same order).
    /// </summary>
    public IReadOnlyList<Guid> ActiveReturnIds { get; set; } = [];

    /// <summary>
    /// True if the ReturnWindowExpired message has already fired.
    /// Used to close the saga when all returns are eventually completed/denied after window expiry.
    /// </summary>
    public bool ReturnWindowFired { get; set; }

    /// <summary>
    /// The delivery timestamp from Fulfillment BC. Used by the BFF
    /// for "Return by {date}" display and cross-verification of return eligibility.
    /// </summary>
    public DateTimeOffset? DeliveredAt { get; set; }

    /// <summary>
    /// Tracking number assigned by Fulfillment BC once a shipping label is generated.
    /// </summary>
    public string? TrackingNumber { get; set; }

    /// <summary>
    /// Number of shipments this order has been split into (multi-FC split orders).
    /// Populated when OrderSplitIntoShipments is received.
    /// </summary>
    public int ShipmentCount { get; set; } = 1;

    /// <summary>
    /// The shipment ID of the active reshipment (if any).
    /// Set when ReshipmentCreated is received.
    /// </summary>
    public Guid? ActiveReshipmentShipmentId { get; set; }

    // NOTE: Saga initialization is performed in PlaceOrderHandler.cs, which constructs the initial
    // Order state and corresponding OrderPlaced event and returns them as a tuple (Order, OrderPlaced)
    // to start the saga. This keeps the saga class focused on state transitions, not initialization logic.
    /// <summary>
    /// Saga handler for order cancellation.
    /// Guard is enforced by the CancelOrderEndpoint HTTP handler before publishing this command.
    /// Business rule: an order can only be cancelled if it has not yet been shipped.
    /// If the saga is in a terminal or post-shipment state, the cancellation is silently ignored
    /// (idempotent behavior for at-least-once delivery scenarios).
    /// Triggers compensation: releases inventory reservations and refunds captured payment.
    /// </summary>
    public OutgoingMessages Handle(CancelOrder command)
    {
        // Guard: silently ignore cancellation requests for orders that cannot be cancelled.
        // The HTTP endpoint pre-validates this, but message bus delivery may not.
        if (!OrderDecider.CanBeCancelled(Status))
        {
            return new OutgoingMessages();
        }

        var decision = OrderDecider.HandleCancelOrder(this, command, DateTimeOffset.UtcNow);

        if (decision.Status.HasValue) Status = decision.Status.Value;

        var outgoing = new OutgoingMessages();
        foreach (var msg in decision.Messages) outgoing.Add(msg);

        // If no payment was captured, there is no RefundCompleted to await.
        // Close the saga immediately. Any late-arriving ReservationReleased messages will be
        // silently discarded by Wolverine (ReservationReleased cannot start a new Order saga).
        if (!IsPaymentCaptured)
            MarkCompleted();

        return outgoing;
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
        if (decision.PaymentId.HasValue) PaymentId = decision.PaymentId.Value;

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
    /// When the order was previously cancelled, a completed refund closes the order lifecycle.
    /// **Validates: Requirement 1.5 - Order tracks refund completion for financial reconciliation**
    /// </summary>
    /// <param name="message">Refund completed integration message from Payments BC.</param>
    public void Handle(RefundCompleted message)
    {
        var decision = OrderDecider.HandleRefundCompleted(this, message);

        if (decision.Status.HasValue) Status = decision.Status.Value;
        if (decision.ShouldComplete) MarkCompleted();
    }

    /// <summary>
    /// Saga handler for failed refund processing.
    /// Refund failures are logged but don't change the order's fulfillment status.
    /// **Validates: Requirement 1.6 - Order tracks refund failures for investigation and retry**
    /// </summary>
    /// <param name="message">Refund failed integration message from Payments BC.</param>
    public void Handle(RefundFailed message)
    {
        var decision = OrderDecider.HandleRefundFailed(this, message);

        if (decision.Status.HasValue) Status = decision.Status.Value;
    }

    /// <summary>
    /// Saga handler for successful inventory reservation (per SKU).
    /// Tracks per-SKU reservation; transitions to InventoryReserved only when ALL SKUs are confirmed.
    /// Orchestrates inventory commitment if payment is already captured and all SKUs reserved.
    /// **Validates: Requirement 2.1 - Order tracks inventory reservation confirmation**
    /// </summary>
    /// <param name="message">Reservation confirmed integration message from Inventory BC.</param>
    /// <returns>Orchestration message(s) if payment is already captured and all inventory is reserved.</returns>
    public OutgoingMessages Handle(ReservationConfirmed message)
    {
        var decision = OrderDecider.HandleReservationConfirmed(this, message, DateTimeOffset.UtcNow);

        // Apply state changes from decision
        if (decision.Status.HasValue) Status = decision.Status.Value;
        if (decision.ConfirmedReservationCount.HasValue)
            ConfirmedReservationCount = decision.ConfirmedReservationCount.Value;
        if (decision.ReservationIds != null) ReservationIds = decision.ReservationIds;

        var outgoing = new OutgoingMessages();
        foreach (var msg in decision.Messages) outgoing.Add(msg);
        return outgoing;
    }

    /// <summary>
    /// Saga handler for inventory reservation failure (insufficient stock).
    /// Transitions order to OutOfStock status and triggers compensation:
    /// - Refunds captured payment (if applicable)
    /// - Releases any other successfully-reserved inventory
    /// **Validates: Requirement 2.2 - Order fails when inventory cannot be reserved**
    /// </summary>
    /// <param name="message">Reservation failed integration message from Inventory BC.</param>
    /// <returns>Compensation messages (refund + release other reservations).</returns>
    public OutgoingMessages Handle(ReservationFailed message)
    {
        var decision = OrderDecider.HandleReservationFailed(this, message, DateTimeOffset.UtcNow);

        if (decision.Status.HasValue) Status = decision.Status.Value;

        var outgoing = new OutgoingMessages();
        foreach (var msg in decision.Messages) outgoing.Add(msg);
        return outgoing;
    }

    /// <summary>
    /// Saga handler for inventory commitment (hard allocation, per SKU).
    /// Tracks per-SKU commits; proceeds to fulfillment only when ALL SKUs committed and payment captured.
    /// Idempotency guard prevents duplicate FulfillmentRequested messages.
    /// **Validates: Requirement 2.3 - Order proceeds after inventory is committed**
    /// **Validates: Requirement 3.1 - Order cannot proceed to fulfillment without confirmed payment**
    /// </summary>
    /// <param name="message">Reservation committed integration message from Inventory BC.</param>
    /// <returns>Orchestration message to start fulfillment if all conditions are met.</returns>
    public OutgoingMessages Handle(ReservationCommitted message)
    {
        var decision = OrderDecider.HandleReservationCommitted(this, message, DateTimeOffset.UtcNow);

        // Apply state changes from decision
        if (decision.Status.HasValue) Status = decision.Status.Value;
        if (decision.CommittedReservationIds != null) CommittedReservationIds = decision.CommittedReservationIds;

        // Return outgoing messages
        var outgoing = new OutgoingMessages();
        foreach (var msg in decision.Messages) outgoing.Add(msg);
        return outgoing;
    }

    /// <summary>
    /// Saga handler for inventory reservation release (compensation acknowledgement).
    /// No status change - release is a side effect of cancellation or failure compensation.
    /// **Validates: Requirement 2.4 - Order tracks inventory release for compensation**
    /// </summary>
    /// <param name="message">Reservation released integration message from Inventory BC.</param>
    public void Handle(ReservationReleased message)
    {
        var decision = OrderDecider.HandleReservationReleased(this, message);

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

        if (decision.Status.HasValue) Status = decision.Status.Value;
    }

    /// <summary>
    /// Saga handler for successful delivery from Fulfillment BC.
    /// Transitions order to Delivered status and schedules a ReturnWindowExpired message.
    /// The saga remains open during the return window to handle potential return requests.
    /// **Validates: Requirement 3.3 - Order completes when delivery confirmed**
    /// </summary>
    /// <param name="message">Shipment delivered integration message from Fulfillment BC.</param>
    /// <returns>Scheduled ReturnWindowExpired message to close the saga after the return window.</returns>
    public OutgoingMessages Handle(FulfillmentMessages.ShipmentDelivered message)
    {
        // Idempotency guard: if the saga is already Delivered or Closed, a duplicate delivery
        // message must not schedule an additional ReturnWindowExpired, which would re-open
        // the return window and/or call MarkCompleted() a second time.
        if (Status is OrderStatus.Delivered or OrderStatus.Closed)
            return new OutgoingMessages();

        DeliveredAt = message.DeliveredAt;

        var decision = OrderDecider.HandleShipmentDelivered(this, message);

        if (decision.Status.HasValue) Status = decision.Status.Value;

        var outgoing = new OutgoingMessages();
        foreach (var msg in decision.Messages) outgoing.Add(msg);

        // Schedule the return window timeout. The saga stays open to handle return requests;
        // when the window expires with no return, Handle(ReturnWindowExpired) closes the saga.
        outgoing.Delay(new ReturnWindowExpired(Id), OrderDecider.ReturnWindowDuration);

        return outgoing;
    }

    /// <summary>
    /// Saga handler for delivery failure from Fulfillment BC.
    /// Tracks delivery issues - order remains in Shipped status (carrier will retry).
    /// **Validates: Requirement 3.4 - Order tracks delivery failures**
    /// </summary>
    /// <param name="message">Shipment delivery failed integration message from Fulfillment BC.</param>
    public void Handle(FulfillmentMessages.ShipmentDeliveryFailed message)
    {
        var decision = OrderDecider.HandleShipmentDeliveryFailed(this, message);

        if (decision.Status.HasValue) Status = decision.Status.Value;
    }

    /// <summary>
    /// Saga handler for carrier custody transfer from Fulfillment BC.
    /// Replaces legacy ShipmentDispatched — transitions order to Shipped.
    /// </summary>
    public void Handle(FulfillmentMessages.ShipmentHandedToCarrier message)
    {
        var decision = OrderDecider.HandleShipmentHandedToCarrier(this, message);

        if (decision.Status.HasValue) Status = decision.Status.Value;
    }

    /// <summary>
    /// Saga handler for tracking number assignment from Fulfillment BC.
    /// Stores the tracking number on the saga for customer-facing queries.
    /// </summary>
    public void Handle(FulfillmentMessages.TrackingNumberAssigned message)
    {
        var decision = OrderDecider.HandleTrackingNumberAssigned(this, message);

        if (decision.TrackingNumber != null) TrackingNumber = decision.TrackingNumber;
    }

    /// <summary>
    /// Saga handler for return-to-sender from Fulfillment BC.
    /// All delivery attempts exhausted; carrier returning package. Order transitions to DeliveryFailed.
    /// </summary>
    public void Handle(FulfillmentMessages.ReturnToSenderInitiated message)
    {
        var decision = OrderDecider.HandleReturnToSenderInitiated(this, message);

        if (decision.Status.HasValue) Status = decision.Status.Value;
    }

    /// <summary>
    /// Saga handler for reshipment creation from Fulfillment BC.
    /// A replacement shipment has been created. Transitions order to Reshipping.
    /// </summary>
    public void Handle(FulfillmentMessages.ReshipmentCreated message)
    {
        var decision = OrderDecider.HandleReshipmentCreated(this, message);

        if (decision.Status.HasValue) Status = decision.Status.Value;
        if (decision.ActiveReshipmentShipmentId.HasValue)
            ActiveReshipmentShipmentId = decision.ActiveReshipmentShipmentId;
    }

    /// <summary>
    /// Saga handler for backorder creation from Fulfillment BC.
    /// Stock unavailable at all FCs; fulfillment paused pending replenishment.
    /// </summary>
    public void Handle(FulfillmentMessages.BackorderCreated message)
    {
        var decision = OrderDecider.HandleBackorderCreated(this, message);

        if (decision.Status.HasValue) Status = decision.Status.Value;
    }

    /// <summary>
    /// Saga handler for fulfillment cancellation from Fulfillment BC.
    /// Triggers compensation: refund if payment captured, release inventory reservations.
    /// </summary>
    public OutgoingMessages Handle(FulfillmentMessages.FulfillmentCancelled message)
    {
        // Guard: only cancel if not already in a terminal state
        if (!OrderDecider.CanBeCancelled(Status))
            return new OutgoingMessages();

        var decision = OrderDecider.HandleFulfillmentCancelled(this, message, DateTimeOffset.UtcNow);

        if (decision.Status.HasValue) Status = decision.Status.Value;

        var outgoing = new OutgoingMessages();
        foreach (var msg in decision.Messages) outgoing.Add(msg);

        // If no payment was captured, there is no RefundCompleted to await.
        // Close the saga immediately.
        if (!IsPaymentCaptured)
            MarkCompleted();

        return outgoing;
    }

    /// <summary>
    /// Saga handler for order split notification from Fulfillment BC.
    /// Informational — stores the shipment count for customer-facing display.
    /// </summary>
    public void Handle(FulfillmentMessages.OrderSplitIntoShipments message)
    {
        var decision = OrderDecider.HandleOrderSplitIntoShipments(this, message);

        if (decision.ShipmentCount.HasValue) ShipmentCount = decision.ShipmentCount.Value;
    }

    /// <summary>
    /// Saga handler for return window expiration.
    /// The return window is scheduled at delivery time. When it fires, the order
    /// moves to Closed status and the saga completes — unless returns are still in progress.
    /// Supports multiple concurrent returns: saga stays open until all returns terminate.
    /// </summary>
    /// <param name="message">Return window expired scheduled message.</param>
    public void Handle(ReturnWindowExpired message)
    {
        ReturnWindowFired = true;
        if (ActiveReturnIds.Count > 0) return; // Stay open; return completion will close the saga
        Status = OrderStatus.Closed;
        MarkCompleted();
    }

    /// <summary>
    /// Saga handler for return request initiation from Returns BC.
    /// Adds the return to the active list to prevent premature saga closure when ReturnWindowExpired fires.
    /// Supports multiple concurrent returns from the same order.
    /// </summary>
    public void Handle(Messages.Contracts.Returns.ReturnRequested message)
    {
        // Add this return to active list (immutable update pattern)
        var activeReturns = ActiveReturnIds.ToList();
        if (!activeReturns.Contains(message.ReturnId))
        {
            activeReturns.Add(message.ReturnId);
            ActiveReturnIds = activeReturns.AsReadOnly();
        }
    }

    /// <summary>
    /// Saga handler for return completion from Returns BC.
    /// Publishes a refund request to Payments BC and removes the return from active list.
    /// Closes the saga only when all returns are terminal AND return window has expired.
    /// Supports multiple concurrent returns (sequential refunds).
    /// </summary>
    public OutgoingMessages Handle(Messages.Contracts.Returns.ReturnCompleted message)
    {
        // Remove this return from active list (immutable update pattern)
        var activeReturns = ActiveReturnIds.ToList();
        activeReturns.Remove(message.ReturnId);
        ActiveReturnIds = activeReturns.AsReadOnly();

        var outgoing = new OutgoingMessages();

        // Defensive guard: only request refund if amount is positive
        // (edge case: all items failed inspection or zero-value return)
        if (message.FinalRefundAmount > 0m)
        {
            outgoing.Add(new Messages.Contracts.Payments.RefundRequested(
                Id,
                message.FinalRefundAmount,
                "Customer return approved and completed",
                DateTimeOffset.UtcNow));
        }

        // Close saga if: (1) no active returns remaining, AND (2) return window already expired
        if (ActiveReturnIds.Count == 0 && ReturnWindowFired)
        {
            Status = OrderStatus.Closed;
            MarkCompleted();
        }

        return outgoing;
    }

    /// <summary>
    /// Saga handler for return denial from Returns BC.
    /// Removes the return from active list. If all returns terminal AND return window expired, closes saga.
    /// </summary>
    public void Handle(Messages.Contracts.Returns.ReturnDenied message)
    {
        // Remove this return from active list (immutable update pattern)
        var activeReturns = ActiveReturnIds.ToList();
        activeReturns.Remove(message.ReturnId);
        ActiveReturnIds = activeReturns.AsReadOnly();

        // Close saga if: (1) no active returns remaining, AND (2) return window already expired
        if (ActiveReturnIds.Count == 0 && ReturnWindowFired)
        {
            Status = OrderStatus.Closed;
            MarkCompleted();
        }
    }

    /// <summary>
    /// Saga handler for return rejection from Returns BC (inspection failed).
    /// Removes the return from active list. If all returns terminal AND return window expired, closes saga.
    /// </summary>
    public void Handle(Messages.Contracts.Returns.ReturnRejected message)
    {
        // Remove this return from active list (immutable update pattern)
        var activeReturns = ActiveReturnIds.ToList();
        activeReturns.Remove(message.ReturnId);
        ActiveReturnIds = activeReturns.AsReadOnly();

        // Close saga if: (1) no active returns remaining, AND (2) return window already expired
        if (ActiveReturnIds.Count == 0 && ReturnWindowFired)
        {
            Status = OrderStatus.Closed;
            MarkCompleted();
        }
    }

    /// <summary>
    /// Saga handler for return expiration from Returns BC.
    /// Customer never shipped; removes return from active list and closes saga if window already fired.
    /// </summary>
    public void Handle(Messages.Contracts.Returns.ReturnExpired message)
    {
        // Remove this return from active list (immutable update pattern)
        var activeReturns = ActiveReturnIds.ToList();
        activeReturns.Remove(message.ReturnId);
        ActiveReturnIds = activeReturns.AsReadOnly();

        // Close saga if: (1) no active returns remaining, AND (2) return window already expired
        if (ActiveReturnIds.Count == 0 && ReturnWindowFired)
        {
            Status = OrderStatus.Closed;
            MarkCompleted();
        }
    }
}
