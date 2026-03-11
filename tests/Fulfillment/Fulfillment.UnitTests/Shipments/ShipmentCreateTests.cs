namespace Fulfillment.UnitTests.Shipments;

/// <summary>
/// Unit tests for <see cref="Shipment.Create"/>.
/// Verifies that initial shipment state is correctly mapped from a <see cref="FulfillmentRequested"/> event.
/// </summary>
public class ShipmentCreateTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static readonly ShippingAddress DefaultAddress = new(
        AddressLine1: "123 Main St",
        AddressLine2: null,
        City: "Springfield",
        StateProvince: "IL",
        PostalCode: "62701",
        Country: "US");

    private static FulfillmentRequested BuildEvent(
        Guid? orderId = null,
        Guid? customerId = null,
        ShippingAddress? address = null,
        IReadOnlyList<FulfillmentLineItem>? lineItems = null,
        string shippingMethod = "Standard",
        DateTimeOffset? requestedAt = null) =>
        new(
            OrderId: orderId ?? Guid.NewGuid(),
            CustomerId: customerId ?? Guid.NewGuid(),
            ShippingAddress: address ?? DefaultAddress,
            LineItems: lineItems ?? [new FulfillmentLineItem("DOG-FOOD-5LB", 2)],
            ShippingMethod: shippingMethod,
            RequestedAt: requestedAt ?? DateTimeOffset.UtcNow);

    // ---------------------------------------------------------------------------
    // Shipment.Create() — field mapping
    // ---------------------------------------------------------------------------

    /// <summary>OrderId must be mapped from the event.</summary>
    [Fact]
    public void Create_Sets_OrderId_From_Event()
    {
        var orderId = Guid.NewGuid();
        var shipment = Shipment.Create(BuildEvent(orderId: orderId));

        shipment.OrderId.ShouldBe(orderId);
    }

    /// <summary>CustomerId must be mapped from the event.</summary>
    [Fact]
    public void Create_Sets_CustomerId_From_Event()
    {
        var customerId = Guid.NewGuid();
        var shipment = Shipment.Create(BuildEvent(customerId: customerId));

        shipment.CustomerId.ShouldBe(customerId);
    }

    /// <summary>ShippingMethod must be mapped from the event.</summary>
    [Fact]
    public void Create_Sets_ShippingMethod_From_Event()
    {
        var shipment = Shipment.Create(BuildEvent(shippingMethod: "Express"));

        shipment.ShippingMethod.ShouldBe("Express");
    }

    /// <summary>RequestedAt must be mapped from the event timestamp.</summary>
    [Fact]
    public void Create_Sets_RequestedAt_From_Event()
    {
        var timestamp = new DateTimeOffset(2025, 3, 15, 9, 0, 0, TimeSpan.Zero);
        var shipment = Shipment.Create(BuildEvent(requestedAt: timestamp));

        shipment.RequestedAt.ShouldBe(timestamp);
    }

    /// <summary>A new shipment always starts in the Pending status.</summary>
    [Fact]
    public void Create_Sets_Status_To_Pending()
    {
        var shipment = Shipment.Create(BuildEvent());

        shipment.Status.ShouldBe(ShipmentStatus.Pending);
    }

    /// <summary>Shipment address fields are mapped correctly from the event.</summary>
    [Fact]
    public void Create_Maps_ShippingAddress_From_Event()
    {
        var address = new ShippingAddress("456 Oak Ave", "Apt 2B", "Chicago", "IL", "60601", "US");
        var shipment = Shipment.Create(BuildEvent(address: address));

        shipment.ShippingAddress.AddressLine1.ShouldBe("456 Oak Ave");
        shipment.ShippingAddress.AddressLine2.ShouldBe("Apt 2B");
        shipment.ShippingAddress.City.ShouldBe("Chicago");
        shipment.ShippingAddress.StateProvince.ShouldBe("IL");
        shipment.ShippingAddress.PostalCode.ShouldBe("60601");
        shipment.ShippingAddress.Country.ShouldBe("US");
    }

    /// <summary>Line items are mapped from the event.</summary>
    [Fact]
    public void Create_Maps_LineItems_From_Event()
    {
        var lineItems = new List<FulfillmentLineItem>
        {
            new("SKU-001", 3),
            new("SKU-002", 1)
        };
        var shipment = Shipment.Create(BuildEvent(lineItems: lineItems));

        shipment.LineItems.Count.ShouldBe(2);
        shipment.LineItems[0].Sku.ShouldBe("SKU-001");
        shipment.LineItems[0].Quantity.ShouldBe(3);
        shipment.LineItems[1].Sku.ShouldBe("SKU-002");
        shipment.LineItems[1].Quantity.ShouldBe(1);
    }

    /// <summary>All optional fields are null immediately after creation.</summary>
    [Fact]
    public void Create_Optional_Fields_Are_Null()
    {
        var shipment = Shipment.Create(BuildEvent());

        shipment.WarehouseId.ShouldBeNull();
        shipment.Carrier.ShouldBeNull();
        shipment.TrackingNumber.ShouldBeNull();
        shipment.AssignedAt.ShouldBeNull();
        shipment.DispatchedAt.ShouldBeNull();
        shipment.DeliveredAt.ShouldBeNull();
        shipment.FailureReason.ShouldBeNull();
    }

    /// <summary>
    /// The shipment Id is <see cref="Guid.Empty"/> immediately after Create — Marten sets it
    /// from the stream key when replaying the event stream. Pure-function Create() never
    /// generates an Id.
    /// </summary>
    [Fact]
    public void Create_Id_Is_Empty_Guid_Before_Marten_Sets_It()
    {
        var shipment = Shipment.Create(BuildEvent());

        shipment.Id.ShouldBe(Guid.Empty);
    }
}
