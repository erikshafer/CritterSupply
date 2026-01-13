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
    /// Decides to create a new Order saga from CheckoutCompleted.
    /// Pure function - accepts time as parameter for true purity.
    /// </summary>
    /// <param name="command">The checkout completed event from Shopping context.</param>
    /// <param name="timestamp">Current timestamp for event timestamping.</param>
    /// <returns>A tuple of the new Order saga and the OrderPlaced event to publish.</returns>
    public static (Order, IntegrationMessages.OrderPlaced) Start(
        CheckoutCompleted command,
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
            PlacedAt = timestamp
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
    /// Decides how to handle successful payment capture.
    /// Pure function - returns new state and messages.
    /// </summary>
    public static OrderDecision HandlePaymentCaptured(
        Order current,
        PaymentCaptured message,
        DateTimeOffset timestamp)
    {
        var decision = new OrderDecision
        {
            Status = OrderStatus.PaymentConfirmed,
            IsPaymentCaptured = true
        };

        // Orchestration: If inventory is already reserved, tell Inventory to commit all reservations
        if (current.IsInventoryReserved)
        {
            foreach (var reservationId in current.ReservationIds.Keys)
            {
                decision.Messages.Add(new IntegrationMessages.ReservationCommitRequested(
                    current.Id,
                    reservationId,
                    timestamp));
            }
        }

        return decision;
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
        var decision = new OrderDecision
        {
            Status = OrderStatus.PaymentFailed
        };

        // Compensation: Release any reserved inventory
        if (current.IsInventoryReserved)
        {
            foreach (var reservationId in current.ReservationIds.Keys)
            {
                decision.Messages.Add(new IntegrationMessages.ReservationReleaseRequested(
                    current.Id,
                    reservationId,
                    "Payment failed",
                    timestamp));
            }
        }

        return decision;
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
    /// Pure function - maintains current status (refund is a financial operation, not order lifecycle).
    /// </summary>
    public static OrderDecision HandleRefundCompleted(
        Order current,
        RefundCompleted message)
    {
        // Refund completed - no order status change, just acknowledge
        return new OrderDecision(); // No state changes
    }

    /// <summary>
    /// Decides how to handle refund failure.
    /// Pure function - maintains current status.
    /// </summary>
    public static OrderDecision HandleRefundFailed(
        Order current,
        RefundFailed message)
    {
        // Refund failed - no order status change, failure is tracked elsewhere
        return new OrderDecision(); // No state changes
    }

    /// <summary>
    /// Decides how to handle inventory reservation confirmation.
    /// Pure function - returns new state and orchestration messages.
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

        var decision = new OrderDecision
        {
            Status = OrderStatus.InventoryReserved,
            IsInventoryReserved = true,
            ReservationIds = newReservations
        };

        // Orchestration: If payment is already captured, tell Inventory to commit this reservation
        if (current.IsPaymentCaptured)
        {
            decision.Messages.Add(new IntegrationMessages.ReservationCommitRequested(
                current.Id,
                message.ReservationId,
                timestamp));
        }

        return decision;
    }

    /// <summary>
    /// Decides how to handle inventory reservation failure.
    /// Pure function - returns new state.
    /// </summary>
    public static OrderDecision HandleReservationFailed(
        Order current,
        ReservationFailed message)
    {
        return new OrderDecision
        {
            Status = OrderStatus.InventoryFailed
            // TODO: Future enhancement - trigger compensation (release payment, cancel order)
        };
    }

    /// <summary>
    /// Decides how to handle inventory commitment (hard allocation).
    /// Pure function - returns new state and fulfillment orchestration if ready.
    /// </summary>
    public static OrderDecision HandleReservationCommitted(
        Order current,
        ReservationCommitted message,
        DateTimeOffset timestamp)
    {
        var decision = new OrderDecision
        {
            Status = OrderStatus.InventoryCommitted
        };

        // Orchestration: If payment is captured, proceed to fulfillment
        if (current.IsPaymentCaptured)
        {
            // Update status to Fulfilling when requesting fulfillment
            decision = decision with { Status = OrderStatus.Fulfilling };

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

            decision.Messages.Add(new FulfillmentMessages.FulfillmentRequested(
                current.Id,
                current.CustomerId,
                shippingAddress,
                fulfillmentLineItems,
                current.ShippingMethod,
                timestamp));
        }

        return decision;
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
        return new OrderDecision(); // No state changes
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
    /// Pure function - returns new state and saga completion signal.
    /// </summary>
    public static OrderDecision HandleShipmentDelivered(
        Order current,
        FulfillmentMessages.ShipmentDelivered message)
    {
        return new OrderDecision
        {
            Status = OrderStatus.Delivered,
            ShouldComplete = true // Signal saga completion
        };
    }

    /// <summary>
    /// Decides how to handle shipment delivery failure.
    /// Pure function - maintains Shipped status (no backward transition).
    /// </summary>
    public static OrderDecision HandleShipmentDeliveryFailed(
        Order current,
        FulfillmentMessages.ShipmentDeliveryFailed message)
    {
        // Order remains in Shipped status - delivery failures don't reverse shipment
        return new OrderDecision(); // No state changes
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
    public bool? IsInventoryReserved { get; init; }
    public Dictionary<Guid, string>? ReservationIds { get; init; }
    public bool ShouldComplete { get; init; }
    public List<object> Messages { get; init; } = new();
}
