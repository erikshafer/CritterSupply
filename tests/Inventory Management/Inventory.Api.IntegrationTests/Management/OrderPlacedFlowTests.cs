using Inventory.Management;
using Marten;
using Messages.Contracts.Orders;

namespace Inventory.Api.IntegrationTests.Management;

/// <summary>
/// Integration tests for OrderPlaced event handling.
/// Tests the choreography flow: OrderPlaced → ReserveStock → inventory reservation.
/// </summary>
[Collection("inventory-integration")]
public class OrderPlacedFlowTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public OrderPlacedFlowTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Integration test: OrderPlaced event triggers inventory reservation.
    /// Validates the choreography pattern where Inventory BC reacts to Orders BC events.
    /// </summary>
    [Fact]
    public async Task OrderPlaced_Triggers_Inventory_Reservation()
    {
        // Arrange: Initialize inventory for test SKU
        var sku = "SKU-TEST-001";
        var warehouseId = "WH-01";
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, warehouseId, 100));

        // Act: Publish OrderPlaced event (simulating Orders BC)
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var lineItems = new List<OrderLineItem>
        {
            new(sku, 5, 29.99m, 149.95m)
        };
        var shippingAddress = new ShippingAddress(
            "123 Test St",
            null,
            "Seattle",
            "WA",
            "98101",
            "US");

        var orderPlaced = new OrderPlaced(
            orderId,
            customerId,
            lineItems,
            shippingAddress,
            "Standard",
            "tok_test_12345",
            149.95m,
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(orderPlaced);

        // Assert: Verify inventory was reserved
        await using var session = _fixture.GetDocumentSession();
        var inventory = await session.Query<ProductInventory>()
            .FirstAsync(i => i.SKU == sku && i.WarehouseId == warehouseId);

        inventory.AvailableQuantity.ShouldBe(95); // 100 - 5
        inventory.ReservedQuantity.ShouldBe(5);
        inventory.Reservations.Count.ShouldBe(1);

        // Verify the reservation is linked to the order
        var reservation = inventory.Reservations.First();
        inventory.ReservationOrderIds[reservation.Key].ShouldBe(orderId);
    }

    /// <summary>
    /// Integration test: OrderPlaced with multiple line items for same SKU aggregates quantity.
    /// Validates that duplicate SKUs in an order result in a single reservation with summed quantity.
    /// </summary>
    [Fact]
    public async Task OrderPlaced_With_Duplicate_SKUs_Aggregates_Quantity()
    {
        // Arrange: Initialize inventory
        var sku = "SKU-TEST-002";
        var warehouseId = "WH-01";
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, warehouseId, 100));

        // Act: Publish OrderPlaced with duplicate SKUs
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var lineItems = new List<OrderLineItem>
        {
            new(sku, 3, 29.99m, 89.97m),
            new(sku, 7, 29.99m, 209.93m) // Same SKU, different quantity
        };
        var shippingAddress = new ShippingAddress(
            "456 Test Ave",
            null,
            "Portland",
            "OR",
            "97201",
            "US");

        var orderPlaced = new OrderPlaced(
            orderId,
            customerId,
            lineItems,
            shippingAddress,
            "Express",
            "tok_test_67890",
            299.90m,
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(orderPlaced);

        // Assert: Verify single reservation with aggregated quantity
        await using var session = _fixture.GetDocumentSession();
        var inventory = await session.Query<ProductInventory>()
            .FirstAsync(i => i.SKU == sku && i.WarehouseId == warehouseId);

        inventory.AvailableQuantity.ShouldBe(90); // 100 - (3 + 7)
        inventory.ReservedQuantity.ShouldBe(10);
        inventory.Reservations.Count.ShouldBe(1); // Single reservation, not two
        inventory.Reservations.First().Value.ShouldBe(10); // Aggregated quantity
    }

    /// <summary>
    /// Integration test: OrderPlaced with multiple different SKUs creates separate reservations.
    /// Validates that different SKUs result in distinct reservations.
    /// </summary>
    [Fact]
    public async Task OrderPlaced_With_Multiple_SKUs_Creates_Separate_Reservations()
    {
        // Arrange: Initialize inventory for multiple SKUs
        var sku1 = "SKU-TEST-003";
        var sku2 = "SKU-TEST-004";
        var warehouseId = "WH-01";
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku1, warehouseId, 100));
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku2, warehouseId, 50));

        // Act: Publish OrderPlaced with multiple SKUs
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var lineItems = new List<OrderLineItem>
        {
            new(sku1, 5, 29.99m, 149.95m),
            new(sku2, 3, 19.99m, 59.97m)
        };
        var shippingAddress = new ShippingAddress(
            "789 Test Blvd",
            "Apt 3",
            "Denver",
            "CO",
            "80201",
            "US");

        var orderPlaced = new OrderPlaced(
            orderId,
            customerId,
            lineItems,
            shippingAddress,
            "Standard",
            "tok_test_11111",
            209.92m,
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(orderPlaced);

        // Assert: Verify separate reservations
        await using var session = _fixture.GetDocumentSession();
        var inventory1 = await session.Query<ProductInventory>()
            .FirstAsync(i => i.SKU == sku1 && i.WarehouseId == warehouseId);
        var inventory2 = await session.Query<ProductInventory>()
            .FirstAsync(i => i.SKU == sku2 && i.WarehouseId == warehouseId);

        // Verify first SKU
        inventory1.AvailableQuantity.ShouldBe(95);
        inventory1.ReservedQuantity.ShouldBe(5);
        inventory1.Reservations.Count.ShouldBe(1);

        // Verify second SKU
        inventory2.AvailableQuantity.ShouldBe(47);
        inventory2.ReservedQuantity.ShouldBe(3);
        inventory2.Reservations.Count.ShouldBe(1);
    }
}
