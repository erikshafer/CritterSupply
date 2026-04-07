using Orders.Placement;
using FulfillmentContracts = Messages.Contracts.Fulfillment;

namespace Orders.UnitTests.Placement;

/// <summary>
/// Unit tests for shipment-related <see cref="OrderDecider"/> methods:
/// <list type="bullet">
///   <item><see cref="OrderDecider.HandleShipmentHandedToCarrier"/></item>
///   <item><see cref="OrderDecider.HandleShipmentDelivered"/></item>
///   <item><see cref="OrderDecider.HandleTrackingNumberAssigned"/></item>
///   <item><see cref="OrderDecider.HandleReturnToSenderInitiated"/></item>
///   <item><see cref="OrderDecider.HandleReshipmentCreated"/></item>
///   <item><see cref="OrderDecider.HandleBackorderCreated"/></item>
///   <item><see cref="OrderDecider.HandleFulfillmentCancelled"/></item>
///   <item><see cref="OrderDecider.HandleOrderSplitIntoShipments"/></item>
/// </list>
/// Note: <see cref="ReturnWindowExpired"/> scheduling is performed by the saga handler using
/// <c>OutgoingMessages.Delay()</c> and is therefore tested via integration tests, not unit tests.
/// </summary>
public class OrderDeciderFulfillmentTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static Order BuildOrder(OrderStatus status = OrderStatus.Fulfilling)
    {
        return new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Status = status,
            IsPaymentCaptured = true,
            ShippingAddress = new ShippingAddress("1 Depot Rd", null, "Memphis", "TN", "38101", "US"),
            ShippingMethod = "Freight",
            TotalAmount = 200.00m,
            ExpectedReservationCount = 1,
        };
    }

    private static FulfillmentContracts.ShipmentHandedToCarrier BuildHandedToCarrier(Guid? orderId = null) =>
        new(orderId ?? Guid.NewGuid(),
            Guid.NewGuid(),
            "FedEx",
            "1Z999AA10123456784",
            DateTimeOffset.UtcNow);

    private static FulfillmentContracts.ShipmentDelivered BuildDelivered(
        Guid? orderId = null,
        string? recipientName = null) =>
        new(orderId ?? Guid.NewGuid(),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            recipientName);

    private static FulfillmentContracts.TrackingNumberAssigned BuildTrackingNumberAssigned(
        Guid? orderId = null,
        string trackingNumber = "1Z999AA10123456784") =>
        new(orderId ?? Guid.NewGuid(),
            Guid.NewGuid(),
            trackingNumber,
            "UPS",
            DateTimeOffset.UtcNow);

    private static FulfillmentContracts.ReturnToSenderInitiated BuildReturnToSender(Guid? orderId = null) =>
        new(orderId ?? Guid.NewGuid(),
            Guid.NewGuid(),
            "FedEx",
            3,
            7,
            DateTimeOffset.UtcNow);

    // ===========================================================================
    // HandleShipmentHandedToCarrier
    // ===========================================================================

    /// <summary>HandleShipmentHandedToCarrier must set Status to Shipped.</summary>
    [Fact]
    public void HandleShipmentHandedToCarrier_SetsStatus_ToShipped()
    {
        var order = BuildOrder(OrderStatus.Fulfilling);

        var decision = OrderDecider.HandleShipmentHandedToCarrier(order, BuildHandedToCarrier(order.Id));

        decision.Status.ShouldBe(OrderStatus.Shipped);
    }

    /// <summary>HandleShipmentHandedToCarrier must emit no messages (pure status transition).</summary>
    [Fact]
    public void HandleShipmentHandedToCarrier_EmitsNoMessages()
    {
        var order = BuildOrder(OrderStatus.Fulfilling);

        var decision = OrderDecider.HandleShipmentHandedToCarrier(order, BuildHandedToCarrier(order.Id));

        decision.Messages.ShouldBeEmpty();
    }

    /// <summary>HandleShipmentHandedToCarrier is idempotent when already Shipped.</summary>
    [Fact]
    public void HandleShipmentHandedToCarrier_AlreadyShipped_ReturnsNoOp()
    {
        var order = BuildOrder(OrderStatus.Shipped);

        var decision = OrderDecider.HandleShipmentHandedToCarrier(order, BuildHandedToCarrier(order.Id));

        decision.Status.ShouldBeNull();
    }

    /// <summary>HandleShipmentHandedToCarrier transitions from Reshipping to Shipped (reshipment dispatched).</summary>
    [Fact]
    public void HandleShipmentHandedToCarrier_FromReshipping_TransitionsToShipped()
    {
        var order = BuildOrder(OrderStatus.Reshipping);

        var decision = OrderDecider.HandleShipmentHandedToCarrier(order, BuildHandedToCarrier(order.Id));

        decision.Status.ShouldBe(OrderStatus.Shipped);
    }

    // ===========================================================================
    // HandleShipmentDelivered
    // ===========================================================================

    /// <summary>HandleShipmentDelivered must set Status to Delivered.</summary>
    [Fact]
    public void HandleShipmentDelivered_SetsStatus_ToDelivered()
    {
        var order = BuildOrder(OrderStatus.Shipped);

        var decision = OrderDecider.HandleShipmentDelivered(order, BuildDelivered(order.Id));

        decision.Status.ShouldBe(OrderStatus.Delivered);
    }

    /// <summary>
    /// HandleShipmentDelivered must emit no messages from the Decider layer.
    /// ReturnWindowExpired scheduling is an infrastructure concern handled by the saga handler.
    /// </summary>
    [Fact]
    public void HandleShipmentDelivered_EmitsNoMessages_ReturnWindowScheduledBySagaHandler()
    {
        var order = BuildOrder(OrderStatus.Shipped);

        var decision = OrderDecider.HandleShipmentDelivered(order, BuildDelivered(order.Id));

        decision.Messages.ShouldBeEmpty();
    }

    /// <summary>HandleShipmentDelivered must not signal saga completion (return window stays open).</summary>
    [Fact]
    public void HandleShipmentDelivered_DoesNotSignalCompletion()
    {
        var order = BuildOrder(OrderStatus.Shipped);

        var decision = OrderDecider.HandleShipmentDelivered(order, BuildDelivered(order.Id));

        decision.ShouldComplete.ShouldBeFalse();
    }

    /// <summary>Delivery works correctly whether or not a recipient name is provided.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("Jane Doe")]
    public void HandleShipmentDelivered_SetsStatusToDelivered_WithOrWithoutRecipientName(string? recipientName)
    {
        var order = BuildOrder(OrderStatus.Shipped);

        var decision = OrderDecider.HandleShipmentDelivered(order, BuildDelivered(order.Id, recipientName));

        decision.Status.ShouldBe(OrderStatus.Delivered);
    }

    // ===========================================================================
    // HandleTrackingNumberAssigned
    // ===========================================================================

    /// <summary>HandleTrackingNumberAssigned stores tracking number with no status change.</summary>
    [Fact]
    public void HandleTrackingNumberAssigned_StoresTrackingNumber()
    {
        var order = BuildOrder(OrderStatus.Fulfilling);

        var decision = OrderDecider.HandleTrackingNumberAssigned(
            order, BuildTrackingNumberAssigned(order.Id, "1Z999BB20456789012"));

        decision.TrackingNumber.ShouldBe("1Z999BB20456789012");
        decision.Status.ShouldBeNull();
    }

    // ===========================================================================
    // HandleReturnToSenderInitiated
    // ===========================================================================

    /// <summary>HandleReturnToSenderInitiated transitions from Shipped to DeliveryFailed.</summary>
    [Fact]
    public void HandleReturnToSenderInitiated_SetsStatus_ToDeliveryFailed()
    {
        var order = BuildOrder(OrderStatus.Shipped);

        var decision = OrderDecider.HandleReturnToSenderInitiated(order, BuildReturnToSender(order.Id));

        decision.Status.ShouldBe(OrderStatus.DeliveryFailed);
    }

    /// <summary>HandleReturnToSenderInitiated is idempotent when already DeliveryFailed.</summary>
    [Fact]
    public void HandleReturnToSenderInitiated_AlreadyDeliveryFailed_ReturnsNoOp()
    {
        var order = BuildOrder(OrderStatus.DeliveryFailed);

        var decision = OrderDecider.HandleReturnToSenderInitiated(order, BuildReturnToSender(order.Id));

        decision.Status.ShouldBeNull();
    }

    // ===========================================================================
    // HandleReshipmentCreated
    // ===========================================================================

    /// <summary>HandleReshipmentCreated transitions to Reshipping and sets the new shipment ID.</summary>
    [Fact]
    public void HandleReshipmentCreated_SetsStatus_ToReshipping()
    {
        var order = BuildOrder(OrderStatus.DeliveryFailed);
        var newShipmentId = Guid.NewGuid();

        var decision = OrderDecider.HandleReshipmentCreated(order,
            new FulfillmentContracts.ReshipmentCreated(order.Id, Guid.NewGuid(), newShipmentId, "Reship", DateTimeOffset.UtcNow));

        decision.Status.ShouldBe(OrderStatus.Reshipping);
        decision.ActiveReshipmentShipmentId.ShouldBe(newShipmentId);
    }

    // ===========================================================================
    // HandleBackorderCreated
    // ===========================================================================

    /// <summary>HandleBackorderCreated transitions to Backordered.</summary>
    [Fact]
    public void HandleBackorderCreated_SetsStatus_ToBackordered()
    {
        var order = BuildOrder(OrderStatus.Fulfilling);

        var decision = OrderDecider.HandleBackorderCreated(order,
            new FulfillmentContracts.BackorderCreated(order.Id, Guid.NewGuid(), "No stock", DateTimeOffset.UtcNow));

        decision.Status.ShouldBe(OrderStatus.Backordered);
    }

    // ===========================================================================
    // HandleFulfillmentCancelled
    // ===========================================================================

    /// <summary>HandleFulfillmentCancelled cancels order and triggers refund when payment captured.</summary>
    [Fact]
    public void HandleFulfillmentCancelled_WithPayment_TriggersRefund()
    {
        var order = BuildOrder(OrderStatus.Fulfilling);
        order.IsPaymentCaptured = true;
        order.TotalAmount = 100.00m;

        var decision = OrderDecider.HandleFulfillmentCancelled(order,
            new FulfillmentContracts.FulfillmentCancelled(order.Id, Guid.NewGuid(), "FC closed", DateTimeOffset.UtcNow),
            DateTimeOffset.UtcNow);

        decision.Status.ShouldBe(OrderStatus.Cancelled);
        decision.Messages.ShouldContain(m => m is Messages.Contracts.Payments.RefundRequested);
        decision.Messages.ShouldContain(m => m is Messages.Contracts.Orders.OrderCancelled);
    }

    /// <summary>HandleFulfillmentCancelled does not trigger refund when payment not captured.</summary>
    [Fact]
    public void HandleFulfillmentCancelled_WithoutPayment_NoRefund()
    {
        var order = BuildOrder(OrderStatus.Fulfilling);
        order.IsPaymentCaptured = false;

        var decision = OrderDecider.HandleFulfillmentCancelled(order,
            new FulfillmentContracts.FulfillmentCancelled(order.Id, Guid.NewGuid(), "FC closed", DateTimeOffset.UtcNow),
            DateTimeOffset.UtcNow);

        decision.Status.ShouldBe(OrderStatus.Cancelled);
        decision.Messages.ShouldNotContain(m => m is Messages.Contracts.Payments.RefundRequested);
        decision.Messages.ShouldContain(m => m is Messages.Contracts.Orders.OrderCancelled);
    }

    /// <summary>HandleFulfillmentCancelled is a no-op for terminal states.</summary>
    [Theory]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Closed)]
    [InlineData(OrderStatus.Cancelled)]
    public void HandleFulfillmentCancelled_TerminalState_ReturnsNoOp(OrderStatus status)
    {
        var order = BuildOrder(status);

        var decision = OrderDecider.HandleFulfillmentCancelled(order,
            new FulfillmentContracts.FulfillmentCancelled(order.Id, Guid.NewGuid(), "FC closed", DateTimeOffset.UtcNow),
            DateTimeOffset.UtcNow);

        decision.Status.ShouldBeNull();
        decision.Messages.ShouldBeEmpty();
    }

    // ===========================================================================
    // HandleOrderSplitIntoShipments
    // ===========================================================================

    /// <summary>HandleOrderSplitIntoShipments stores shipment count with no status change.</summary>
    [Fact]
    public void HandleOrderSplitIntoShipments_StoresShipmentCount()
    {
        var order = BuildOrder(OrderStatus.Fulfilling);

        var decision = OrderDecider.HandleOrderSplitIntoShipments(order,
            new FulfillmentContracts.OrderSplitIntoShipments(order.Id, 3, DateTimeOffset.UtcNow));

        decision.ShipmentCount.ShouldBe(3);
        decision.Status.ShouldBeNull();
    }

    // ===========================================================================
    // ReturnWindowDuration constant
    // ===========================================================================

    /// <summary>The return window duration must be exactly 30 days as per business rules.</summary>
    [Fact]
    public void ReturnWindowDuration_Is_ThirtyDays()
    {
        OrderDecider.ReturnWindowDuration.ShouldBe(TimeSpan.FromDays(30));
    }
}
