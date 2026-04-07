using Messages.Contracts.Inventory;
using Messages.Contracts.Payments;
using IntegrationMessages = Messages.Contracts.Orders;
using FulfillmentMessages = Messages.Contracts.Fulfillment;
using CommonMessages = Messages.Contracts.Common;

namespace Orders.Placement;

/// <summary>
/// Pure functions implementing the Decider pattern for Order saga.
/// Contains all business logic for order lifecycle decisions.
/// These functions have no side effects and are easily testable.
/// </summary>
public static class OrderDecider
{
    /// <summary>
    /// How long after delivery the return window stays open before the saga closes automatically.
    /// </summary>
    public static readonly TimeSpan ReturnWindowDuration = TimeSpan.FromDays(30);

    /// <summary>
    /// Decides to create a new Order saga from PlaceOrder command.
    /// Pure function - accepts time as parameter for true purity.
    /// </summary>
    /// <param name="command">The place order command (mapped from Shopping BC's CartCheckoutCompleted).</param>
    /// <param name="timestamp">Current timestamp for event timestamping.</param>
    /// <returns>A tuple of the new Order saga and the OrderPlaced event to publish.</returns>
    public static (Order, IntegrationMessages.OrderPlaced) Start(
        PlaceOrder command,
        DateTimeOffset timestamp)
    {
        var orderId = command.OrderId;

        var lineItems = command.LineItems
            .Select(item => new OrderLineItem(
                item.Sku,
                item.Quantity,
                item.PriceAtPurchase,
                item.Quantity * item.PriceAtPurchase))
            .ToList();

        var subtotal = lineItems.Sum(x => x.LineTotal);
        var totalAmount = subtotal + command.ShippingCost;

        // Track how many distinct SKUs expect an individual reservation response.
        // Inventory BC creates one reservation per distinct SKU in OrderPlacedHandler.
        var expectedReservationCount = command.LineItems
            .Select(li => li.Sku)
            .Distinct()
            .Count();

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
            PlacedAt = timestamp,
            ExpectedReservationCount = expectedReservationCount
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
            timestamp);

        return (saga, orderPlaced);
    }

    /// <summary>
    /// Returns true if the order is in a state that allows cancellation.
    /// Used by the HTTP endpoint for pre-flight validation and by the saga handler for idempotency.
    /// Orders in terminal states are excluded to prevent duplicate compensation messages.
    /// Note: Shipped is cancellable since FulfillmentCancelled won't fire post-carrier-handoff,
    /// and DeliveryFailed/Reshipping/Backordered are cancellable as fulfillment has not succeeded.
    /// </summary>
    public static bool CanBeCancelled(OrderStatus status) =>
        status is not (OrderStatus.Delivered or OrderStatus.Closed
            or OrderStatus.Cancelled or OrderStatus.OutOfStock
            or OrderStatus.PaymentFailed);

    /// <summary>
    /// Decides how to handle an order cancellation request.
    /// Pure function - returns new state and compensation messages.
    /// Guard conditions (cannot cancel after Shipped) are validated via CanBeCancelled().
    /// </summary>
    public static OrderDecision HandleCancelOrder(
        Order current,
        CancelOrder command,
        DateTimeOffset timestamp)
    {
        var messages = new List<object>();

        // Compensation: Release all reserved inventory
        foreach (var reservationId in current.ReservationIds.Keys)
        {
            messages.Add(new IntegrationMessages.ReservationReleaseRequested(
                current.Id,
                reservationId,
                command.Reason,
                timestamp));
        }

        // Compensation: Refund if payment was captured
        if (current.IsPaymentCaptured)
        {
            messages.Add(new Messages.Contracts.Payments.RefundRequested(
                current.Id,
                current.TotalAmount,
                command.Reason,
                timestamp));
        }

        // Publish OrderCancelled integration event for downstream BCs
        messages.Add(new IntegrationMessages.OrderCancelled(
            current.Id,
            current.CustomerId,
            command.Reason,
            timestamp));

        return new OrderDecision
        {
            Status = OrderStatus.Cancelled,
            Messages = messages
        };
    }

    /// <summary>
    /// Decides how to handle successful payment capture.
    /// Pure function - returns new state and messages.
    /// </summary>
    public static OrderDecision HandlePaymentCaptured(
        Order current,
        PaymentCaptured message,
        DateTimeOffset timestamp)
    {
        var messages = new List<object>();

        // Orchestration: If ALL inventory is already reserved, tell Inventory to commit all reservations
        if (current.IsInventoryReserved)
        {
            foreach (var reservationId in current.ReservationIds.Keys)
            {
                messages.Add(new IntegrationMessages.ReservationCommitRequested(
                    current.Id,
                    reservationId,
                    timestamp));
            }
        }

        return new OrderDecision
        {
            Status = OrderStatus.PaymentConfirmed,
            IsPaymentCaptured = true,
            PaymentId = message.PaymentId,
            Messages = messages
        };
    }

    /// <summary>
    /// Decides how to handle payment failure.
    /// Pure function - returns new state and compensation messages.
    /// </summary>
    public static OrderDecision HandlePaymentFailed(
        Order current,
        PaymentFailed message,
        DateTimeOffset timestamp)
    {
        var messages = new List<object>();

        // Compensation: Release any reserved inventory
        if (current.IsInventoryReserved)
        {
            foreach (var reservationId in current.ReservationIds.Keys)
            {
                messages.Add(new IntegrationMessages.ReservationReleaseRequested(
                    current.Id,
                    reservationId,
                    "Payment failed",
                    timestamp));
            }
        }

        return new OrderDecision
        {
            Status = OrderStatus.PaymentFailed,
            Messages = messages
        };
    }

    /// <summary>
    /// Decides how to handle payment authorization (two-phase payment).
    /// Pure function - returns new state.
    /// </summary>
    public static OrderDecision HandlePaymentAuthorized(
        Order current,
        PaymentAuthorized message)
    {
        return new OrderDecision
        {
            Status = OrderStatus.PendingPayment
        };
    }

    /// <summary>
    /// Decides how to handle refund completion.
    /// When the order was cancelled or went out of stock (both trigger RefundRequested if payment
    /// was captured), a completed refund signals the financial lifecycle is closed.
    /// </summary>
    public static OrderDecision HandleRefundCompleted(
        Order current,
        RefundCompleted message)
    {
        // Cancelled orders and OutOfStock orders both emit RefundRequested when payment was captured.
        // A completed refund for either closes the financial lifecycle and deletes the saga.
        if (current.Status is OrderStatus.Cancelled or OrderStatus.OutOfStock)
        {
            return new OrderDecision
            {
                Status = OrderStatus.Closed,
                ShouldComplete = true
            };
        }

        return new OrderDecision(); // No state changes for refunds on non-cancelled/non-failed orders
    }

    /// <summary>
    /// Decides how to handle refund failure.
    /// Pure function - maintains current status.
    /// </summary>
    public static OrderDecision HandleRefundFailed(
        Order current,
        RefundFailed message)
    {
        // Refund failed - no order status change, failure is tracked by Payments BC for retry
        return new OrderDecision();
    }

    /// <summary>
    /// Decides how to handle inventory reservation confirmation (per SKU).
    /// Tracks per-SKU confirmations; only signals InventoryReserved when ALL SKUs are confirmed.
    /// Idempotent: duplicate confirmations for the same ReservationId are ignored.
    /// When payment is already captured, issues commit requests for ALL confirmed reservations to
    /// handle the race where payment arrived between individual reservation confirmations.
    /// </summary>
    public static OrderDecision HandleReservationConfirmed(
        Order current,
        ReservationConfirmed message,
        DateTimeOffset timestamp)
    {
        // Terminal-state guard: don't process reservation confirmations for orders that have already
        // self-compensated or been cancelled. Without this, a late-arriving ReservationConfirmed on a
        // cancelled order with IsPaymentCaptured=true would issue spurious ReservationCommitRequested
        // messages, directing Inventory BC to hard-allocate stock for a cancelled order.
        if (current.Status is OrderStatus.Cancelled or OrderStatus.OutOfStock
            or OrderStatus.PaymentFailed or OrderStatus.Closed)
            return new OrderDecision();

        // Idempotency guard: if this reservation was already confirmed, return a no-op.
        // Without this, a redelivered ReservationConfirmed would increment ConfirmedReservationCount
        // beyond the expected count, causing IsInventoryReserved to trigger prematurely.
        if (current.ReservationIds.ContainsKey(message.ReservationId))
            return new OrderDecision();

        var newReservations = new Dictionary<Guid, string>(current.ReservationIds)
        {
            [message.ReservationId] = message.Sku
        };

        // Defensive fallback: if ExpectedReservationCount was not set at saga start (e.g., legacy
        // in-flight documents), derive from line item count so orchestration gates still work.
        var effectiveExpectedReservationCount =
            current.ExpectedReservationCount == 0
                ? current.LineItems.Count
                : current.ExpectedReservationCount;

        var newConfirmedCount = current.ConfirmedReservationCount + 1;
        var allReserved = newConfirmedCount >= effectiveExpectedReservationCount;

        var messages = new List<object>();

        // Orchestration: If payment is already captured, commit ALL confirmed reservations.
        // This covers the race where payment arrived between individual reservation confirmations:
        // e.g., R1 confirmed (no payment) → payment captured (IsInventoryReserved=false, no commits issued yet)
        //        → R2 confirmed (payment already captured, only R2 committed) → R1 stuck uncommitted.
        // By committing all entries in newReservations.Keys here, we ensure every prior reservation
        // also gets a commit request. The Inventory BC must handle duplicate commit requests idempotently,
        // and HandleReservationCommitted guards against double-counting via CommittedReservationIds.
        if (current.IsPaymentCaptured)
        {
            foreach (var reservationId in newReservations.Keys)
            {
                messages.Add(new IntegrationMessages.ReservationCommitRequested(
                    current.Id,
                    reservationId,
                    timestamp));
            }
        }

        return new OrderDecision
        {
            Status = allReserved ? OrderStatus.InventoryReserved : current.Status,
            ConfirmedReservationCount = newConfirmedCount,
            ReservationIds = newReservations,
            Messages = messages
        };
    }

    /// <summary>
    /// Decides how to handle inventory reservation failure (insufficient stock).
    /// Triggers compensation: cancels the order, refunds captured payment, and releases
    /// any reservations that did succeed before the failure was reported.
    /// </summary>
    public static OrderDecision HandleReservationFailed(
        Order current,
        ReservationFailed message,
        DateTimeOffset timestamp)
    {
        var messages = new List<object>();

        // Compensation: Refund captured payment — we cannot fulfill, so money must be returned
        if (current.IsPaymentCaptured)
        {
            messages.Add(new Messages.Contracts.Payments.RefundRequested(
                current.Id,
                current.TotalAmount,
                $"Inventory unavailable for SKU {message.Sku}: {message.Reason}",
                timestamp));
        }

        // Compensation: Release any other reservations that did succeed
        foreach (var reservationId in current.ReservationIds.Keys)
        {
            messages.Add(new IntegrationMessages.ReservationReleaseRequested(
                current.Id,
                reservationId,
                $"Order cancelled due to inventory unavailability: {message.Reason}",
                timestamp));
        }

        return new OrderDecision
        {
            Status = OrderStatus.OutOfStock,
            Messages = messages
        };
    }

    /// <summary>
    /// Decides how to handle inventory commitment (hard allocation, per SKU).
    /// Tracks per-SKU commits via CommittedReservationIds; only dispatches FulfillmentRequested
    /// when ALL SKUs are committed and payment has been captured.
    /// Idempotent: duplicate ReservationCommitted messages for the same ReservationId are ignored,
    /// preventing premature fulfillment dispatch or over-incrementing of the committed count.
    /// </summary>
    public static OrderDecision HandleReservationCommitted(
        Order current,
        ReservationCommitted message,
        DateTimeOffset timestamp)
    {
        // Idempotency guard: ignore duplicate ReservationCommitted for a reservation we already processed.
        // This can happen naturally under at-least-once delivery, and also because HandleReservationConfirmed
        // (with IsPaymentCaptured=true) re-issues commit requests for ALL confirmed reservations to
        // handle the payment-between-reservations race condition.
        if (current.CommittedReservationIds.Contains(message.ReservationId))
            return new OrderDecision();

        var newCommittedIds = new HashSet<Guid>(current.CommittedReservationIds) { message.ReservationId };
        var newCommittedCount = newCommittedIds.Count;
        var allCommitted = newCommittedCount >= current.ExpectedReservationCount;

        var messages = new List<object>();

        // Dispatch fulfillment only when ALL reservations are committed and payment is captured.
        // Idempotency guards: exclude all terminal and post-fulfillment states to prevent
        // duplicate FulfillmentRequested messages from late or duplicate events.
        if (allCommitted
            && current.IsPaymentCaptured
            && current.Status != OrderStatus.Fulfilling
            && current.Status != OrderStatus.Shipped
            && current.Status != OrderStatus.Delivered
            && current.Status != OrderStatus.Closed
            && current.Status != OrderStatus.Cancelled
            && current.Status != OrderStatus.OutOfStock)
        {
            var fulfillmentLineItems = current.LineItems
                .Select(li => new FulfillmentMessages.FulfillmentLineItem(
                    li.Sku,
                    li.Quantity))
                .ToList();

            var shippingAddress = new CommonMessages.SharedShippingAddress
            {
                AddressLine1 = current.ShippingAddress.Street,
                AddressLine2 = current.ShippingAddress.Street2,
                City = current.ShippingAddress.City,
                StateProvince = current.ShippingAddress.State,
                PostalCode = current.ShippingAddress.PostalCode,
                Country = current.ShippingAddress.Country
            };

            messages.Add(new FulfillmentMessages.FulfillmentRequested(
                current.Id,
                current.CustomerId,
                shippingAddress,
                fulfillmentLineItems,
                current.ShippingMethod,
                timestamp));
        }

        var newStatus = allCommitted && current.IsPaymentCaptured
            ? OrderStatus.Fulfilling
            : OrderStatus.InventoryCommitted;

        return new OrderDecision
        {
            Status = newStatus,
            CommittedReservationIds = newCommittedIds,
            Messages = messages
        };
    }

    /// <summary>
    /// Decides how to handle reservation release (compensation).
    /// Pure function - returns new state.
    /// </summary>
    public static OrderDecision HandleReservationReleased(
        Order current,
        ReservationReleased message)
    {
        // Compensation acknowledged - no status change needed
        return new OrderDecision();
    }

    /// <summary>
    /// Decides how to handle shipment dispatch.
    /// Pure function - returns new state.
    /// </summary>
    public static OrderDecision HandleShipmentDispatched(
        Order current,
        FulfillmentMessages.ShipmentDispatched message)
    {
        return new OrderDecision
        {
            Status = OrderStatus.Shipped
        };
    }

    /// <summary>
    /// Decides how to handle shipment delivery.
    /// Transitions to Delivered. The saga remains open after delivery to allow returns;
    /// the ReturnWindowExpired message is scheduled by the saga handler via OutgoingMessages.Delay().
    /// </summary>
    public static OrderDecision HandleShipmentDelivered(
        Order current,
        FulfillmentMessages.ShipmentDelivered message)
    {
        return new OrderDecision
        {
            Status = OrderStatus.Delivered
            // Note: ReturnWindowExpired is scheduled in the saga handler using OutgoingMessages.Delay()
            // to keep the Decider free of infrastructure concerns (scheduling duration, delivery mechanism).
        };
    }

    /// <summary>
    /// Decides how to handle shipment delivery failure.
    /// Pure function - maintains Shipped status (carrier will retry delivery).
    /// </summary>
    public static OrderDecision HandleShipmentDeliveryFailed(
        Order current,
        FulfillmentMessages.ShipmentDeliveryFailed message)
    {
        // Order remains in Shipped status - carrier retries delivery
        return new OrderDecision();
    }

    /// <summary>
    /// Decides how to handle carrier custody transfer (replaces ShipmentDispatched).
    /// Pure function - transitions to Shipped when carrier takes possession.
    /// </summary>
    public static OrderDecision HandleShipmentHandedToCarrier(
        Order current,
        FulfillmentMessages.ShipmentHandedToCarrier message)
    {
        // Idempotency: already Shipped or in terminal state
        if (current.Status is OrderStatus.Shipped or OrderStatus.Delivered
            or OrderStatus.Closed)
            return new OrderDecision();

        return new OrderDecision { Status = OrderStatus.Shipped };
    }

    /// <summary>
    /// Decides how to handle tracking number assignment.
    /// Pure function - stores tracking number, no status change.
    /// </summary>
    public static OrderDecision HandleTrackingNumberAssigned(
        Order current,
        FulfillmentMessages.TrackingNumberAssigned message)
    {
        return new OrderDecision { TrackingNumber = message.TrackingNumber };
    }

    /// <summary>
    /// Decides how to handle return-to-sender initiation.
    /// All delivery attempts exhausted; carrier returning package.
    /// Transitions to DeliveryFailed. Saga stays open for potential reshipment.
    /// </summary>
    public static OrderDecision HandleReturnToSenderInitiated(
        Order current,
        FulfillmentMessages.ReturnToSenderInitiated message)
    {
        // Idempotency: already in DeliveryFailed or beyond
        if (current.Status is OrderStatus.DeliveryFailed or OrderStatus.Reshipping
            or OrderStatus.Closed or OrderStatus.Cancelled)
            return new OrderDecision();

        return new OrderDecision { Status = OrderStatus.DeliveryFailed };
    }

    /// <summary>
    /// Decides how to handle reshipment creation.
    /// A replacement shipment has been created. Transitions to Reshipping.
    /// </summary>
    public static OrderDecision HandleReshipmentCreated(
        Order current,
        FulfillmentMessages.ReshipmentCreated message)
    {
        return new OrderDecision
        {
            Status = OrderStatus.Reshipping,
            ActiveReshipmentShipmentId = message.NewShipmentId
        };
    }

    /// <summary>
    /// Decides how to handle backorder creation.
    /// Stock unavailable at all FCs. Fulfillment paused pending replenishment.
    /// </summary>
    public static OrderDecision HandleBackorderCreated(
        Order current,
        FulfillmentMessages.BackorderCreated message)
    {
        return new OrderDecision { Status = OrderStatus.Backordered };
    }

    /// <summary>
    /// Decides how to handle fulfillment cancellation.
    /// Triggers compensation: refund if payment captured, release inventory.
    /// </summary>
    public static OrderDecision HandleFulfillmentCancelled(
        Order current,
        FulfillmentMessages.FulfillmentCancelled message,
        DateTimeOffset timestamp)
    {
        // Guard: only cancel if not already in a terminal state
        if (!CanBeCancelled(current.Status))
            return new OrderDecision();

        var messages = new List<object>();

        if (current.IsPaymentCaptured)
        {
            messages.Add(new Messages.Contracts.Payments.RefundRequested(
                current.Id,
                current.TotalAmount,
                $"Fulfillment cancelled: {message.Reason}",
                timestamp));
        }

        foreach (var reservationId in current.ReservationIds.Keys)
        {
            messages.Add(new IntegrationMessages.ReservationReleaseRequested(
                current.Id,
                reservationId,
                $"Fulfillment cancelled: {message.Reason}",
                timestamp));
        }

        messages.Add(new IntegrationMessages.OrderCancelled(
            current.Id,
            current.CustomerId,
            $"Fulfillment cancelled: {message.Reason}",
            timestamp));

        return new OrderDecision
        {
            Status = OrderStatus.Cancelled,
            Messages = messages
        };
    }

    /// <summary>
    /// Decides how to handle order split notification.
    /// Informational — stores shipment count for customer-facing display.
    /// </summary>
    public static OrderDecision HandleOrderSplitIntoShipments(
        Order current,
        FulfillmentMessages.OrderSplitIntoShipments message)
    {
        return new OrderDecision { ShipmentCount = message.ShipmentCount };
    }
}

/// <summary>
/// Represents a decision made by the OrderDecider.
/// Contains state changes and outgoing messages.
/// </summary>
public sealed record OrderDecision
{
    public OrderStatus? Status { get; init; }
    public bool? IsPaymentCaptured { get; init; }
    public Guid? PaymentId { get; init; }
    public int? ConfirmedReservationCount { get; init; }
    public Dictionary<Guid, string>? ReservationIds { get; init; }
    public HashSet<Guid>? CommittedReservationIds { get; init; }
    public bool ShouldComplete { get; init; }
    public IReadOnlyList<object> Messages { get; init; } = [];
    public string? TrackingNumber { get; init; }
    public Guid? ActiveReshipmentShipmentId { get; init; }
    public int? ShipmentCount { get; init; }
}
