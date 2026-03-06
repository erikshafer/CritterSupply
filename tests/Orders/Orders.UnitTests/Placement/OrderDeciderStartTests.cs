using FsCheck.Xunit;
using Orders.Placement;
using IntegrationContracts = Messages.Contracts.Orders;

namespace Orders.UnitTests.Placement;

/// <summary>
/// Unit tests for <see cref="OrderDecider.Start"/>.
/// Verifies that a new Order saga and its corresponding <see cref="IntegrationContracts.OrderPlaced"/>
/// integration event are produced correctly from a <see cref="PlaceOrder"/> command.
/// </summary>
public class OrderDeciderStartTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static PlaceOrder BuildCommand(
        Guid? orderId = null,
        Guid? customerId = null,
        IReadOnlyList<CheckoutLineItem>? lineItems = null,
        ShippingAddress? address = null,
        string shippingMethod = "Standard",
        decimal shippingCost = 9.99m,
        string paymentToken = "tok_test_123")
    {
        return new PlaceOrder(
            orderId ?? Guid.NewGuid(),
            Guid.NewGuid(),
            customerId ?? Guid.NewGuid(),
            lineItems ?? [new CheckoutLineItem("SKU-001", 2, 10.00m)],
            address ?? DefaultAddress(),
            shippingMethod,
            shippingCost,
            paymentToken,
            DateTimeOffset.UtcNow);
    }

    private static ShippingAddress DefaultAddress() =>
        new("123 Main St", null, "Springfield", "IL", "62701", "US");

    // ---------------------------------------------------------------------------
    // Order saga state tests
    // ---------------------------------------------------------------------------

    /// <summary>A new order saga must always begin in the Placed status.</summary>
    [Fact]
    public void Start_NewOrder_HasStatus_Placed()
    {
        var (order, _) = OrderDecider.Start(BuildCommand(), DateTimeOffset.UtcNow);

        order.Status.ShouldBe(OrderStatus.Placed);
    }

    /// <summary>The saga Id must match the OrderId in the command.</summary>
    [Fact]
    public void Start_SagaId_MatchesCommandOrderId()
    {
        var orderId = Guid.NewGuid();
        var (order, _) = OrderDecider.Start(BuildCommand(orderId: orderId), DateTimeOffset.UtcNow);

        order.Id.ShouldBe(orderId);
    }

    /// <summary>The CustomerId must be carried over from the command.</summary>
    [Fact]
    public void Start_CustomerId_MappedFromCommand()
    {
        var customerId = Guid.NewGuid();
        var (order, _) = OrderDecider.Start(BuildCommand(customerId: customerId), DateTimeOffset.UtcNow);

        order.CustomerId.ShouldBe(customerId);
    }

    /// <summary>
    /// TotalAmount = sum of (Quantity × PriceAtPurchase) for all line items + ShippingCost.
    /// </summary>
    [Fact]
    public void Start_TotalAmount_IsLineItemSumPlusShippingCost()
    {
        var lineItems = new List<CheckoutLineItem>
        {
            new("SKU-001", 2, 10.00m),  // 20.00
            new("SKU-002", 1, 15.50m),  // 15.50
        };
        var shippingCost = 6.49m;
        var command = BuildCommand(lineItems: lineItems, shippingCost: shippingCost);

        var (order, _) = OrderDecider.Start(command, DateTimeOffset.UtcNow);

        order.TotalAmount.ShouldBe(20.00m + 15.50m + 6.49m);
    }

    /// <summary>
    /// Total must always be non-negative when all inputs are non-negative
    /// (property-based sanity check using FsCheck v3 API).
    /// </summary>
    [Property]
    public bool Start_TotalAmount_IsAlwaysNonNegative(FsCheck.PositiveInt unitCents, FsCheck.PositiveInt shippingCents)
    {
        var unitPrice = unitCents.Get / 100.0m;
        var shipping = shippingCents.Get / 100.0m;
        var lineItems = new List<CheckoutLineItem> { new("SKU-X", 1, unitPrice) };
        var command = BuildCommand(lineItems: lineItems, shippingCost: shipping);
        var (order, _) = OrderDecider.Start(command, DateTimeOffset.UtcNow);
        return order.TotalAmount >= 0m;
    }

    /// <summary>
    /// ExpectedReservationCount equals the number of *distinct* SKUs in the line items,
    /// not the raw line-item count.
    /// </summary>
    [Fact]
    public void Start_ExpectedReservationCount_ReflectsDistinctSkus_NotLineItemCount()
    {
        var lineItems = new List<CheckoutLineItem>
        {
            new("SKU-001", 2, 10.00m),
            new("SKU-001", 1, 10.00m),  // duplicate SKU
        };
        var (order, _) = OrderDecider.Start(BuildCommand(lineItems: lineItems), DateTimeOffset.UtcNow);

        order.ExpectedReservationCount.ShouldBe(1);
    }

    /// <summary>Three distinct SKUs produce ExpectedReservationCount of 3.</summary>
    [Fact]
    public void Start_ExpectedReservationCount_CountsAllDistinctSkus()
    {
        var lineItems = new List<CheckoutLineItem>
        {
            new("SKU-A", 1, 5.00m),
            new("SKU-B", 2, 8.00m),
            new("SKU-C", 1, 12.00m),
        };
        var (order, _) = OrderDecider.Start(BuildCommand(lineItems: lineItems), DateTimeOffset.UtcNow);

        order.ExpectedReservationCount.ShouldBe(3);
    }

    /// <summary>All order line items must be mapped from checkout line items with correct totals.</summary>
    [Fact]
    public void Start_LineItems_AreMappedWithCalculatedLineTotals()
    {
        var lineItems = new List<CheckoutLineItem>
        {
            new("SKU-001", 3, 8.00m),
            new("SKU-002", 2, 12.50m),
        };
        var (order, _) = OrderDecider.Start(BuildCommand(lineItems: lineItems), DateTimeOffset.UtcNow);

        order.LineItems.Count.ShouldBe(2);
        order.LineItems[0].Sku.ShouldBe("SKU-001");
        order.LineItems[0].Quantity.ShouldBe(3);
        order.LineItems[0].UnitPrice.ShouldBe(8.00m);
        order.LineItems[0].LineTotal.ShouldBe(24.00m);
        order.LineItems[1].Sku.ShouldBe("SKU-002");
        order.LineItems[1].Quantity.ShouldBe(2);
        order.LineItems[1].UnitPrice.ShouldBe(12.50m);
        order.LineItems[1].LineTotal.ShouldBe(25.00m);
    }

    /// <summary>ShippingAddress, ShippingMethod, PaymentMethodToken must all be mapped from command.</summary>
    [Fact]
    public void Start_ShippingFields_AreMappedFromCommand()
    {
        var address = new ShippingAddress("456 Oak Ave", "Apt 2B", "Chicago", "IL", "60601", "US");
        var command = BuildCommand(address: address, shippingMethod: "Express", paymentToken: "tok_visa_xyz");

        var (order, _) = OrderDecider.Start(command, DateTimeOffset.UtcNow);

        order.ShippingAddress.ShouldBe(address);
        order.ShippingMethod.ShouldBe("Express");
        order.PaymentMethodToken.ShouldBe("tok_visa_xyz");
    }

    /// <summary>PlacedAt must equal the timestamp argument passed to Start.</summary>
    [Fact]
    public void Start_PlacedAt_IsSetToProvidedTimestamp()
    {
        var timestamp = new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var (order, _) = OrderDecider.Start(BuildCommand(), timestamp);

        order.PlacedAt.ShouldBe(timestamp);
    }

    // ---------------------------------------------------------------------------
    // OrderPlaced integration event tests
    // ---------------------------------------------------------------------------

    /// <summary>The integration event OrderId must match the saga Id.</summary>
    [Fact]
    public void Start_OrderPlacedEvent_HasMatchingOrderId()
    {
        var orderId = Guid.NewGuid();
        var (_, orderPlaced) = OrderDecider.Start(BuildCommand(orderId: orderId), DateTimeOffset.UtcNow);

        orderPlaced.OrderId.ShouldBe(orderId);
    }

    /// <summary>The integration event CustomerId must match the command.</summary>
    [Fact]
    public void Start_OrderPlacedEvent_HasMatchingCustomerId()
    {
        var customerId = Guid.NewGuid();
        var (_, orderPlaced) = OrderDecider.Start(BuildCommand(customerId: customerId), DateTimeOffset.UtcNow);

        orderPlaced.CustomerId.ShouldBe(customerId);
    }

    /// <summary>The integration event TotalAmount must equal the same calculated total as the saga.</summary>
    [Fact]
    public void Start_OrderPlacedEvent_TotalAmount_MatchesSagaTotalAmount()
    {
        var lineItems = new List<CheckoutLineItem> { new("SKU-X", 4, 7.50m) };
        var command = BuildCommand(lineItems: lineItems, shippingCost: 3.00m);

        var (order, orderPlaced) = OrderDecider.Start(command, DateTimeOffset.UtcNow);

        orderPlaced.TotalAmount.ShouldBe(order.TotalAmount);
        orderPlaced.TotalAmount.ShouldBe(33.00m); // 4*7.50 + 3.00
    }

    /// <summary>The integration event line items must mirror the command line items with correct totals.</summary>
    [Fact]
    public void Start_OrderPlacedEvent_LineItems_AreMappedCorrectly()
    {
        var lineItems = new List<CheckoutLineItem>
        {
            new("SKU-001", 2, 10.00m),
            new("SKU-002", 1, 25.00m),
        };
        var (_, orderPlaced) = OrderDecider.Start(BuildCommand(lineItems: lineItems), DateTimeOffset.UtcNow);

        orderPlaced.LineItems.Count.ShouldBe(2);
        orderPlaced.LineItems[0].Sku.ShouldBe("SKU-001");
        orderPlaced.LineItems[0].Quantity.ShouldBe(2);
        orderPlaced.LineItems[0].UnitPrice.ShouldBe(10.00m);
        orderPlaced.LineItems[0].LineTotal.ShouldBe(20.00m);
        orderPlaced.LineItems[1].Sku.ShouldBe("SKU-002");
        orderPlaced.LineItems[1].Quantity.ShouldBe(1);
        orderPlaced.LineItems[1].UnitPrice.ShouldBe(25.00m);
        orderPlaced.LineItems[1].LineTotal.ShouldBe(25.00m);
    }

    /// <summary>The integration event ShippingAddress must be fully mapped from the command address.</summary>
    [Fact]
    public void Start_OrderPlacedEvent_ShippingAddress_IsMappedFromCommand()
    {
        var address = new ShippingAddress("789 Pine Rd", "Suite 3", "Austin", "TX", "73301", "US");
        var (_, orderPlaced) = OrderDecider.Start(BuildCommand(address: address), DateTimeOffset.UtcNow);

        orderPlaced.ShippingAddress.Street.ShouldBe("789 Pine Rd");
        orderPlaced.ShippingAddress.Street2.ShouldBe("Suite 3");
        orderPlaced.ShippingAddress.City.ShouldBe("Austin");
        orderPlaced.ShippingAddress.State.ShouldBe("TX");
        orderPlaced.ShippingAddress.PostalCode.ShouldBe("73301");
        orderPlaced.ShippingAddress.Country.ShouldBe("US");
    }

    /// <summary>Optional Street2 is null when not supplied in the command.</summary>
    [Fact]
    public void Start_OrderPlacedEvent_ShippingAddress_Street2_IsNullWhenOmitted()
    {
        var address = new ShippingAddress("1 Road St", null, "Denver", "CO", "80201", "US");
        var (_, orderPlaced) = OrderDecider.Start(BuildCommand(address: address), DateTimeOffset.UtcNow);

        orderPlaced.ShippingAddress.Street2.ShouldBeNull();
    }

    /// <summary>ShippingMethod and PaymentMethodToken on the event must match the command.</summary>
    [Fact]
    public void Start_OrderPlacedEvent_ShippingMethodAndPaymentToken_AreMappedFromCommand()
    {
        var command = BuildCommand(shippingMethod: "Overnight", paymentToken: "tok_amex_999");
        var (_, orderPlaced) = OrderDecider.Start(command, DateTimeOffset.UtcNow);

        orderPlaced.ShippingMethod.ShouldBe("Overnight");
        orderPlaced.PaymentMethodToken.ShouldBe("tok_amex_999");
    }

    /// <summary>PlacedAt on the event must equal the timestamp argument.</summary>
    [Fact]
    public void Start_OrderPlacedEvent_PlacedAt_IsSetToProvidedTimestamp()
    {
        var timestamp = new DateTimeOffset(2024, 3, 20, 10, 30, 0, TimeSpan.Zero);
        var (_, orderPlaced) = OrderDecider.Start(BuildCommand(), timestamp);

        orderPlaced.PlacedAt.ShouldBe(timestamp);
    }
}
