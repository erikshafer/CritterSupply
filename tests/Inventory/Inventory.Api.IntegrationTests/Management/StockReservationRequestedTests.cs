using Inventory.Management;
using Messages.Contracts.Fulfillment;
using IntegrationContracts = Messages.Contracts.Inventory;

namespace Inventory.Api.IntegrationTests.Management;

/// <summary>
/// Integration tests for <see cref="StockReservationRequestedHandler"/> which handles
/// inbound <see cref="StockReservationRequested"/> messages from the Fulfillment BC.
/// Verifies the routing-aware reservation flow (warehouse-specific stock reservation),
/// the at-least-once-delivery idempotency guard, and the
/// <see cref="IntegrationContracts.ReservationConfirmed"/> /
/// <see cref="IntegrationContracts.ReservationFailed"/> outbound contracts.
/// (M43.0 — Slice 12: replaces the legacy OrderPlacedHandler test coverage.)
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class StockReservationRequestedTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public StockReservationRequestedTests(TestFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Sufficient_Stock_Reserves_And_Decrements_Inventory()
    {
        // Arrange: initialize inventory at a specific warehouse
        var sku = "RESREQ-001";
        var warehouseId = "NJ-FC";
        var initialQuantity = 100;

        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, warehouseId, initialQuantity));

        var inventoryId = InventoryStreamId.Compute(sku, warehouseId);
        var orderId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();

        // Act: send the integration message from Fulfillment
        var message = new StockReservationRequested(orderId, sku, warehouseId, reservationId, 40);
        var tracked = await _fixture.ExecuteAndWaitAsync(message);

        // Assert: inventory decremented and reservation tracked
        await using var session = _fixture.GetDocumentSession();
        var inventory = await session.LoadAsync<ProductInventory>(inventoryId);

        inventory.ShouldNotBeNull();
        inventory.AvailableQuantity.ShouldBe(60); // 100 - 40
        inventory.ReservedQuantity.ShouldBe(40);
        inventory.Reservations.ShouldContainKey(reservationId);
        inventory.Reservations[reservationId].ShouldBe(40);
        inventory.TotalOnHand.ShouldBe(100); // conservation invariant

        // Outbound contract: ReservationConfirmed published to Orders BC
        var confirmed = tracked.Sent.SingleMessage<IntegrationContracts.ReservationConfirmed>();
        confirmed.OrderId.ShouldBe(orderId);
        confirmed.ReservationId.ShouldBe(reservationId);
        confirmed.Sku.ShouldBe(sku);
        confirmed.WarehouseId.ShouldBe(warehouseId);
        confirmed.Quantity.ShouldBe(40);
        confirmed.InventoryId.ShouldBe(inventoryId);
    }

    [Fact]
    public async Task Insufficient_Stock_Does_Not_Apply_Reservation_And_Publishes_ReservationFailed()
    {
        // Arrange: initialize inventory with limited stock
        var sku = "RESREQ-FAIL-001";
        var warehouseId = "NJ-FC";
        var initialQuantity = 10;

        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, warehouseId, initialQuantity));

        var inventoryId = InventoryStreamId.Compute(sku, warehouseId);
        var orderId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();

        // Act: attempt to reserve more than available
        var message = new StockReservationRequested(orderId, sku, warehouseId, reservationId, 50);
        var tracked = await _fixture.ExecuteAndWaitAsync(message);

        // Assert: inventory unchanged
        await using var session = _fixture.GetDocumentSession();
        var inventory = await session.LoadAsync<ProductInventory>(inventoryId);

        inventory.ShouldNotBeNull();
        inventory.AvailableQuantity.ShouldBe(initialQuantity); // unchanged
        inventory.ReservedQuantity.ShouldBe(0);
        inventory.Reservations.ShouldBeEmpty();

        // Outbound contract: ReservationFailed published to Orders BC (M43.0).
        // The legacy ProblemDetails return is gone; the failure now flows over the queue.
        var failed = tracked.Sent.SingleMessage<IntegrationContracts.ReservationFailed>();
        failed.OrderId.ShouldBe(orderId);
        failed.ReservationId.ShouldBe(reservationId);
        failed.Sku.ShouldBe(sku);
        failed.WarehouseId.ShouldBe(warehouseId);
        failed.RequestedQuantity.ShouldBe(50);
        failed.AvailableQuantity.ShouldBe(initialQuantity);
        failed.Reason.ShouldContain("Insufficient stock");
    }

    [Fact]
    public async Task Unknown_Inventory_Publishes_ReservationFailed()
    {
        // Arrange: do not initialize any inventory for the SKU/WH pair
        var sku = "RESREQ-UNKNOWN-001";
        var warehouseId = "OH-FC";
        var orderId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();

        // Act
        var message = new StockReservationRequested(orderId, sku, warehouseId, reservationId, 5);
        var tracked = await _fixture.ExecuteAndWaitAsync(message);

        // Assert: ReservationFailed surfaces the missing-inventory case so Orders can compensate
        var failed = tracked.Sent.SingleMessage<IntegrationContracts.ReservationFailed>();
        failed.OrderId.ShouldBe(orderId);
        failed.ReservationId.ShouldBe(reservationId);
        failed.Sku.ShouldBe(sku);
        failed.WarehouseId.ShouldBe(warehouseId);
        failed.RequestedQuantity.ShouldBe(5);
        failed.AvailableQuantity.ShouldBe(0);
        failed.Reason.ShouldContain("No inventory found");
    }

    [Fact]
    public async Task Duplicate_ReservationId_Is_Idempotent()
    {
        // Arrange: at-least-once delivery may redeliver the same ReservationId.
        // The handler must not double-reserve, double-confirm, or double-schedule expiry.
        var sku = "RESREQ-IDEMPOTENT-001";
        var warehouseId = "NJ-FC";

        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, warehouseId, 50));

        var inventoryId = InventoryStreamId.Compute(sku, warehouseId);
        var orderId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var message = new StockReservationRequested(orderId, sku, warehouseId, reservationId, 10);

        // Act: deliver the same message twice
        await _fixture.ExecuteAndWaitAsync(message);
        var trackedSecond = await _fixture.ExecuteAndWaitAsync(message);

        // Assert: only one reservation applied
        await using var session = _fixture.GetDocumentSession();
        var inventory = await session.LoadAsync<ProductInventory>(inventoryId);

        inventory.ShouldNotBeNull();
        inventory.AvailableQuantity.ShouldBe(40); // 50 - 10 (NOT 50 - 20)
        inventory.ReservedQuantity.ShouldBe(10);
        inventory.Reservations.Count.ShouldBe(1);
        inventory.Reservations[reservationId].ShouldBe(10);

        // The redelivered message must NOT publish a duplicate ReservationConfirmed.
        trackedSecond.Sent.MessagesOf<IntegrationContracts.ReservationConfirmed>().ShouldBeEmpty();
        trackedSecond.Sent.MessagesOf<IntegrationContracts.ReservationFailed>().ShouldBeEmpty();
    }

    [Fact]
    public async Task Multi_Sku_Same_Warehouse_Produces_Separate_Reservations()
    {
        // Arrange: two SKUs at the same FC, simulating Fulfillment fanning out
        // one StockReservationRequested per line item for a multi-SKU order.
        var skuA = "RESREQ-MULTI-A";
        var skuB = "RESREQ-MULTI-B";
        var warehouseId = "OH-FC";

        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(skuA, warehouseId, 100));
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(skuB, warehouseId, 50));

        var orderId = Guid.NewGuid();
        var reservationIdA = Guid.NewGuid();
        var reservationIdB = Guid.NewGuid();

        // Act
        await _fixture.ExecuteAndWaitAsync(
            new StockReservationRequested(orderId, skuA, warehouseId, reservationIdA, 5));
        await _fixture.ExecuteAndWaitAsync(
            new StockReservationRequested(orderId, skuB, warehouseId, reservationIdB, 3));

        // Assert: two separate aggregates, each with its own reservation
        await using var session = _fixture.GetDocumentSession();
        var inventoryA = await session.LoadAsync<ProductInventory>(InventoryStreamId.Compute(skuA, warehouseId));
        var inventoryB = await session.LoadAsync<ProductInventory>(InventoryStreamId.Compute(skuB, warehouseId));

        inventoryA.ShouldNotBeNull();
        inventoryA.AvailableQuantity.ShouldBe(95);
        inventoryA.Reservations[reservationIdA].ShouldBe(5);

        inventoryB.ShouldNotBeNull();
        inventoryB.AvailableQuantity.ShouldBe(47);
        inventoryB.Reservations[reservationIdB].ShouldBe(3);
    }
}
