namespace Fulfillment.UnitTests.Shipments;

/// <summary>
/// Unit tests for all <see cref="Shipment.Apply"/> overloads:
/// <see cref="WarehouseAssigned"/>, <see cref="ShipmentDispatched"/>,
/// <see cref="ShipmentDelivered"/>, and <see cref="ShipmentDeliveryFailed"/>.
/// </summary>
public class ShipmentApplyTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static readonly ShippingAddress DefaultAddress = new(
        "123 Main St", null, "Springfield", "IL", "62701", "US");

    private static Shipment BuildPendingShipment(Guid? orderId = null) =>
        Shipment.Create(new FulfillmentRequested(
            OrderId: orderId ?? Guid.NewGuid(),
            CustomerId: Guid.NewGuid(),
            ShippingAddress: DefaultAddress,
            LineItems: [new FulfillmentLineItem("CAT-FOOD-001", 2)],
            ShippingMethod: "Standard",
            RequestedAt: DateTimeOffset.UtcNow));

    private static Shipment BuildAssignedShipment(string warehouseId = "WH-EAST-01")
    {
        var shipment = BuildPendingShipment();
        return shipment.Apply(new WarehouseAssigned(warehouseId, DateTimeOffset.UtcNow));
    }

    private static Shipment BuildShippedShipment(string carrier = "UPS", string tracking = "1Z999AA10123456784")
    {
        var shipment = BuildAssignedShipment();
        return shipment.Apply(new ShipmentDispatched(carrier, tracking, DateTimeOffset.UtcNow));
    }

    // ---------------------------------------------------------------------------
    // Apply(WarehouseAssigned)
    // ---------------------------------------------------------------------------

    /// <summary>WarehouseAssigned transitions status to Assigned.</summary>
    [Fact]
    public void Apply_WarehouseAssigned_Sets_Status_To_Assigned()
    {
        var shipment = BuildPendingShipment();
        var result = shipment.Apply(new WarehouseAssigned("WH-NORTH-01", DateTimeOffset.UtcNow));

        result.Status.ShouldBe(ShipmentStatus.Assigned);
    }

    /// <summary>WarehouseAssigned records the WarehouseId.</summary>
    [Fact]
    public void Apply_WarehouseAssigned_Sets_WarehouseId()
    {
        var shipment = BuildPendingShipment();
        var result = shipment.Apply(new WarehouseAssigned("WH-WEST-99", DateTimeOffset.UtcNow));

        result.WarehouseId.ShouldBe("WH-WEST-99");
    }

    /// <summary>WarehouseAssigned records the AssignedAt timestamp.</summary>
    [Fact]
    public void Apply_WarehouseAssigned_Sets_AssignedAt()
    {
        var assignedAt = new DateTimeOffset(2025, 4, 10, 14, 30, 0, TimeSpan.Zero);
        var shipment = BuildPendingShipment();
        var result = shipment.Apply(new WarehouseAssigned("WH-EAST-01", assignedAt));

        result.AssignedAt.ShouldBe(assignedAt);
    }

    /// <summary>WarehouseAssigned does not set carrier or tracking (those come later).</summary>
    [Fact]
    public void Apply_WarehouseAssigned_Carrier_And_Tracking_Remain_Null()
    {
        var shipment = BuildPendingShipment();
        var result = shipment.Apply(new WarehouseAssigned("WH-EAST-01", DateTimeOffset.UtcNow));

        result.Carrier.ShouldBeNull();
        result.TrackingNumber.ShouldBeNull();
    }

    // ---------------------------------------------------------------------------
    // Apply(ShipmentDispatched)
    // ---------------------------------------------------------------------------

    /// <summary>ShipmentDispatched transitions status to Shipped.</summary>
    [Fact]
    public void Apply_ShipmentDispatched_Sets_Status_To_Shipped()
    {
        var shipment = BuildAssignedShipment();
        var result = shipment.Apply(new ShipmentDispatched("FedEx", "274899996100", DateTimeOffset.UtcNow));

        result.Status.ShouldBe(ShipmentStatus.Shipped);
    }

    /// <summary>ShipmentDispatched records the carrier name.</summary>
    [Fact]
    public void Apply_ShipmentDispatched_Sets_Carrier()
    {
        var shipment = BuildAssignedShipment();
        var result = shipment.Apply(new ShipmentDispatched("USPS", "9400111899223404769718", DateTimeOffset.UtcNow));

        result.Carrier.ShouldBe("USPS");
    }

    /// <summary>ShipmentDispatched records the tracking number.</summary>
    [Fact]
    public void Apply_ShipmentDispatched_Sets_TrackingNumber()
    {
        var trackingNumber = "1Z999AA10123456784";
        var shipment = BuildAssignedShipment();
        var result = shipment.Apply(new ShipmentDispatched("UPS", trackingNumber, DateTimeOffset.UtcNow));

        result.TrackingNumber.ShouldBe(trackingNumber);
    }

    /// <summary>ShipmentDispatched records the DispatchedAt timestamp.</summary>
    [Fact]
    public void Apply_ShipmentDispatched_Sets_DispatchedAt()
    {
        var dispatchedAt = new DateTimeOffset(2025, 5, 1, 8, 0, 0, TimeSpan.Zero);
        var shipment = BuildAssignedShipment();
        var result = shipment.Apply(new ShipmentDispatched("DHL", "9876543210", dispatchedAt));

        result.DispatchedAt.ShouldBe(dispatchedAt);
    }

    // ---------------------------------------------------------------------------
    // Apply(ShipmentDelivered)
    // ---------------------------------------------------------------------------

    /// <summary>ShipmentDelivered transitions status to Delivered.</summary>
    [Fact]
    public void Apply_ShipmentDelivered_Sets_Status_To_Delivered()
    {
        var shipment = BuildShippedShipment();
        var result = shipment.Apply(new ShipmentDelivered(DateTimeOffset.UtcNow));

        result.Status.ShouldBe(ShipmentStatus.Delivered);
    }

    /// <summary>ShipmentDelivered records the DeliveredAt timestamp.</summary>
    [Fact]
    public void Apply_ShipmentDelivered_Sets_DeliveredAt()
    {
        var deliveredAt = new DateTimeOffset(2025, 5, 3, 14, 0, 0, TimeSpan.Zero);
        var shipment = BuildShippedShipment();
        var result = shipment.Apply(new ShipmentDelivered(deliveredAt));

        result.DeliveredAt.ShouldBe(deliveredAt);
    }

    /// <summary>ShipmentDelivered does not set FailureReason.</summary>
    [Fact]
    public void Apply_ShipmentDelivered_FailureReason_Remains_Null()
    {
        var shipment = BuildShippedShipment();
        var result = shipment.Apply(new ShipmentDelivered(DateTimeOffset.UtcNow));

        result.FailureReason.ShouldBeNull();
    }

    // ---------------------------------------------------------------------------
    // Apply(ShipmentDeliveryFailed)
    // ---------------------------------------------------------------------------

    /// <summary>ShipmentDeliveryFailed transitions status to DeliveryFailed.</summary>
    [Fact]
    public void Apply_ShipmentDeliveryFailed_Sets_Status_To_DeliveryFailed()
    {
        var shipment = BuildShippedShipment();
        var result = shipment.Apply(new ShipmentDeliveryFailed("Address not found", DateTimeOffset.UtcNow));

        result.Status.ShouldBe(ShipmentStatus.DeliveryFailed);
    }

    /// <summary>ShipmentDeliveryFailed records the failure reason.</summary>
    [Fact]
    public void Apply_ShipmentDeliveryFailed_Sets_FailureReason()
    {
        var reason = "Recipient unavailable after 3 attempts";
        var shipment = BuildShippedShipment();
        var result = shipment.Apply(new ShipmentDeliveryFailed(reason, DateTimeOffset.UtcNow));

        result.FailureReason.ShouldBe(reason);
    }

    /// <summary>ShipmentDeliveryFailed does not set DeliveredAt.</summary>
    [Fact]
    public void Apply_ShipmentDeliveryFailed_DeliveredAt_Remains_Null()
    {
        var shipment = BuildShippedShipment();
        var result = shipment.Apply(new ShipmentDeliveryFailed("Door locked", DateTimeOffset.UtcNow));

        result.DeliveredAt.ShouldBeNull();
    }

    // ---------------------------------------------------------------------------
    // Immutability — record identity is preserved, only changed fields differ
    // ---------------------------------------------------------------------------

    /// <summary>Applying WarehouseAssigned preserves the OrderId.</summary>
    [Fact]
    public void Apply_WarehouseAssigned_Preserves_OrderId()
    {
        var orderId = Guid.NewGuid();
        var shipment = BuildPendingShipment(orderId: orderId);
        var result = shipment.Apply(new WarehouseAssigned("WH-01", DateTimeOffset.UtcNow));

        result.OrderId.ShouldBe(orderId);
    }

    /// <summary>Applying ShipmentDispatched preserves the WarehouseId set earlier.</summary>
    [Fact]
    public void Apply_ShipmentDispatched_Preserves_WarehouseId()
    {
        var shipment = BuildAssignedShipment(warehouseId: "WH-SOUTH-02");
        var result = shipment.Apply(new ShipmentDispatched("UPS", "TRACK123", DateTimeOffset.UtcNow));

        result.WarehouseId.ShouldBe("WH-SOUTH-02");
    }

    /// <summary>
    /// Applying ShipmentDelivered preserves the Carrier and TrackingNumber set during dispatch.
    /// The delivery event only records DeliveredAt; prior dispatch fields must survive.
    /// </summary>
    [Fact]
    public void Apply_ShipmentDelivered_Preserves_Carrier_And_TrackingNumber()
    {
        var shipment = BuildShippedShipment(carrier: "FedEx", tracking: "274899996100");

        var result = shipment.Apply(new ShipmentDelivered(DateTimeOffset.UtcNow));

        result.Carrier.ShouldBe("FedEx");
        result.TrackingNumber.ShouldBe("274899996100");
    }

    /// <summary>
    /// Applying ShipmentDeliveryFailed preserves the Carrier and TrackingNumber set during dispatch.
    /// The failure event only records the Reason; prior dispatch fields must survive.
    /// </summary>
    [Fact]
    public void Apply_ShipmentDeliveryFailed_Preserves_Carrier_And_TrackingNumber()
    {
        var shipment = BuildShippedShipment(carrier: "UPS", tracking: "1Z999AA10123456784");

        var result = shipment.Apply(new ShipmentDeliveryFailed("No access to building", DateTimeOffset.UtcNow));

        result.Carrier.ShouldBe("UPS");
        result.TrackingNumber.ShouldBe("1Z999AA10123456784");
    }
}
