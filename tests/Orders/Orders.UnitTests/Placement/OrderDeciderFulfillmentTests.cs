using Orders.Placement;
using FulfillmentContracts = Messages.Contracts.Fulfillment;

namespace Orders.UnitTests.Placement;

/// <summary>
/// Unit tests for shipment-related <see cref="OrderDecider"/> methods:
/// <list type="bullet">
///   <item><see cref="OrderDecider.HandleShipmentDispatched"/></item>
///   <item><see cref="OrderDecider.HandleShipmentDelivered"/></item>
///   <item><see cref="OrderDecider.HandleShipmentDeliveryFailed"/></item>
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

    private static FulfillmentContracts.ShipmentDispatched BuildDispatched(Guid? orderId = null) =>
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

    private static FulfillmentContracts.ShipmentDeliveryFailed BuildDeliveryFailed(
        Guid? orderId = null,
        string reason = "Address not found") =>
        new(orderId ?? Guid.NewGuid(),
            Guid.NewGuid(),
            reason,
            DateTimeOffset.UtcNow);

    // ===========================================================================
    // HandleShipmentDispatched
    // ===========================================================================

    /// <summary>HandleShipmentDispatched must set Status to Shipped.</summary>
    [Fact]
    public void HandleShipmentDispatched_SetsStatus_ToShipped()
    {
        var order = BuildOrder(OrderStatus.Fulfilling);

        var decision = OrderDecider.HandleShipmentDispatched(order, BuildDispatched(order.Id));

        decision.Status.ShouldBe(OrderStatus.Shipped);
    }

    /// <summary>HandleShipmentDispatched must emit no messages (pure status transition).</summary>
    [Fact]
    public void HandleShipmentDispatched_EmitsNoMessages()
    {
        var order = BuildOrder(OrderStatus.Fulfilling);

        var decision = OrderDecider.HandleShipmentDispatched(order, BuildDispatched(order.Id));

        decision.Messages.ShouldBeEmpty();
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
    // HandleShipmentDeliveryFailed
    // ===========================================================================

    /// <summary>
    /// HandleShipmentDeliveryFailed must not change order status.
    /// The order remains Shipped so the carrier can retry delivery.
    /// </summary>
    [Fact]
    public void HandleShipmentDeliveryFailed_DoesNotChangeStatus()
    {
        var order = BuildOrder(OrderStatus.Shipped);

        var decision = OrderDecider.HandleShipmentDeliveryFailed(order, BuildDeliveryFailed(order.Id));

        decision.Status.ShouldBeNull();
    }

    /// <summary>HandleShipmentDeliveryFailed must emit no messages.</summary>
    [Fact]
    public void HandleShipmentDeliveryFailed_EmitsNoMessages()
    {
        var order = BuildOrder(OrderStatus.Shipped);

        var decision = OrderDecider.HandleShipmentDeliveryFailed(order, BuildDeliveryFailed(order.Id));

        decision.Messages.ShouldBeEmpty();
    }

    /// <summary>HandleShipmentDeliveryFailed must not signal saga completion.</summary>
    [Fact]
    public void HandleShipmentDeliveryFailed_DoesNotSignalCompletion()
    {
        var order = BuildOrder(OrderStatus.Shipped);

        var decision = OrderDecider.HandleShipmentDeliveryFailed(order, BuildDeliveryFailed());

        decision.ShouldComplete.ShouldBeFalse();
    }

    /// <summary>Delivery failures for different reasons must all return empty decisions.</summary>
    [Theory]
    [InlineData("Address not found")]
    [InlineData("Recipient unavailable")]
    [InlineData("Access denied")]
    public void HandleShipmentDeliveryFailed_VariousReasons_AlwaysReturnsNoOp(string reason)
    {
        var order = BuildOrder(OrderStatus.Shipped);

        var decision = OrderDecider.HandleShipmentDeliveryFailed(order, BuildDeliveryFailed(reason: reason));

        decision.Status.ShouldBeNull();
        decision.Messages.ShouldBeEmpty();
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
