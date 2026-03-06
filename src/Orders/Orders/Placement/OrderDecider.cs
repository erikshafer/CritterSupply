using Messages.Contracts.Inventory;
using Messages.Contracts.Payments;
using IntegrationMessages = Messages.Contracts.Orders;
using FulfillmentMessages = Messages.Contracts.Fulfillment;

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
    /// <param name="command">The place order command (mapped from Shopping BC's CheckoutCompleted).</param>
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
    /// </summary>
    public static bool CanBeCancelled(OrderStatus status) =>
        status is not (OrderStatus.Shipped or OrderStatus.Delivered
            or OrderStatus.Closed or OrderStatus.Cancelled);

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
    /// When the order was cancelled, a completed refund signals the financial lifecycle is closed.
    /// </summary>
    public static OrderDecision HandleRefundCompleted(
        Order current,
        RefundCompleted message)
    {
        // If the order was cancelled, a completed refund closes the lifecycle
        if (current.Status == OrderStatus.Cancelled)
        {
            return new OrderDecision
            {
                Status = OrderStatus.Closed,
                ShouldComplete = true
            };
        }

        return new OrderDecision(); // No state changes for refunds on non-cancelled orders
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
    /// Triggers ReservationCommitRequested for the confirmed SKU only if payment is already captured.
    /// </summary>
    public static OrderDecision HandleReservationConfirmed(
        Order current,
        ReservationConfirmed message,
        DateTimeOffset timestamp)
    {
        var newReservations = new Dictionary<Guid, string>(current.ReservationIds)
        {
            [message.ReservationId] = message.Sku
        };

        var effectiveExpectedReservationCount =
            current.ExpectedReservationCount == 0
                ? current.LineItems.Count
                : current.ExpectedReservationCount;

        var newConfirmedCount = current.ConfirmedReservationCount + 1;
        var allReserved = newConfirmedCount >= effectiveExpectedReservationCount;

        var messages = new List<object>();

        // Orchestration: If payment is already captured, commit this specific reservation immediately.
        // When allReserved is also true, HandlePaymentCaptured would have already issued commit
        // requests for the other reservations — so only commit the newly confirmed one here.
        if (current.IsPaymentCaptured)
        {
            messages.Add(new IntegrationMessages.ReservationCommitRequested(
                current.Id,
                message.ReservationId,
                timestamp));
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
    /// Tracks per-SKU commits; only dispatches FulfillmentRequested when ALL SKUs are committed
    /// and payment has been captured. Idempotency guard prevents duplicate dispatch.
    /// </summary>
    public static OrderDecision HandleReservationCommitted(
        Order current,
        ReservationCommitted message,
        DateTimeOffset timestamp)
    {
        var newCommittedCount = current.CommittedReservationCount + 1;
        var allCommitted = newCommittedCount >= current.ExpectedReservationCount;

        var messages = new List<object>();

        // Dispatch fulfillment only when ALL reservations are committed and payment is captured.
        // Idempotency guard: Status != Fulfilling prevents duplicate FulfillmentRequested messages.
        if (allCommitted && current.IsPaymentCaptured && current.Status != OrderStatus.Fulfilling)
        {
            var fulfillmentLineItems = current.LineItems
                .Select(li => new FulfillmentMessages.FulfillmentLineItem(
                    li.Sku,
                    li.Quantity))
                .ToList();

            var shippingAddress = new FulfillmentMessages.ShippingAddress(
                current.ShippingAddress.Street,
                current.ShippingAddress.Street2,
                current.ShippingAddress.City,
                current.ShippingAddress.State,
                current.ShippingAddress.PostalCode,
                current.ShippingAddress.Country);

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
            CommittedReservationCount = newCommittedCount,
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
}

/// <summary>
/// Represents a decision made by the OrderDecider.
/// Contains state changes and outgoing messages.
/// </summary>
public sealed record OrderDecision
{
    public OrderStatus? Status { get; init; }
    public bool? IsPaymentCaptured { get; init; }
    public int? ConfirmedReservationCount { get; init; }
    public int? CommittedReservationCount { get; init; }
    public Dictionary<Guid, string>? ReservationIds { get; init; }
    public bool ShouldComplete { get; init; }
    public IReadOnlyList<object> Messages { get; init; } = [];
}
