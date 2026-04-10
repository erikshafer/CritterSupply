using Inventory.Management;
using Marten;
using Messages.Contracts.Fulfillment;

namespace Inventory.Api.IntegrationTests.Management;

/// <summary>
/// Integration tests for <see cref="ItemPickedHandler"/> and <see cref="ShipmentHandedToCarrierHandler"/>
/// which handle inbound Fulfillment integration messages for physical pick/ship tracking.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class PhysicalPickShipTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public PhysicalPickShipTests(TestFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ---------------------------------------------------------------------------
    // Shared helpers
    // ---------------------------------------------------------------------------

    private async Task<(Guid inventoryId, Guid reservationId, Guid orderId)> SeedCommittedInventory(
        string sku = "PICK-TEST-001", string warehouseId = "NJ-FC", int initialQty = 100, int reserveQty = 20)
    {
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, warehouseId, initialQty));

        var inventoryId = InventoryStreamId.Compute(sku, warehouseId);
        var orderId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();

        // Reserve and commit
        await _fixture.ExecuteAndWaitAsync(new ReserveStock(orderId, sku, warehouseId, reservationId, reserveQty));
        await _fixture.ExecuteAndWaitAsync(new CommitReservation(inventoryId, reservationId));

        return (inventoryId, reservationId, orderId);
    }

    // ---------------------------------------------------------------------------
    // Slice 13: ItemPicked → StockPicked
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ItemPicked_FullQuantity_AppendsStockPicked_NoDiscrepancy()
    {
        var sku = "PICK-FULL-001";
        var warehouseId = "NJ-FC";
        var (inventoryId, reservationId, orderId) = await SeedCommittedInventory(sku, warehouseId, 100, 20);

        await _fixture.ExecuteAndWaitAsync(
            new ItemPicked(orderId, sku, warehouseId, 20, DateTimeOffset.UtcNow));

        await using var session = _fixture.GetDocumentSession();
        var inventory = await session.LoadAsync<ProductInventory>(inventoryId);

        inventory.ShouldNotBeNull();
        inventory.CommittedAllocations.ShouldNotContainKey(reservationId);
        inventory.PickedAllocations.ShouldContainKey(reservationId);
        inventory.PickedAllocations[reservationId].ShouldBe(20);
        inventory.TotalOnHand.ShouldBe(100); // conserved: 80 avail + 20 picked
    }

    [Fact]
    public async Task ItemPicked_ShortPick_AppendsStockPicked_AndDiscrepancy()
    {
        var sku = "PICK-SHORT-001";
        var warehouseId = "NJ-FC";
        var (inventoryId, reservationId, orderId) = await SeedCommittedInventory(sku, warehouseId, 100, 20);

        // Picker found only 15 of 20 committed
        await _fixture.ExecuteAndWaitAsync(
            new ItemPicked(orderId, sku, warehouseId, 15, DateTimeOffset.UtcNow));

        await using var session = _fixture.GetDocumentSession();
        var inventory = await session.LoadAsync<ProductInventory>(inventoryId);

        inventory.ShouldNotBeNull();
        inventory.PickedAllocations.ShouldContainKey(reservationId);
        inventory.PickedAllocations[reservationId].ShouldBe(15);

        // Verify discrepancy event was appended by checking events on stream
        var events = await session.Events.FetchStreamAsync(inventoryId);
        events.ShouldContain(e => e.EventType == typeof(StockDiscrepancyFound));
    }

    [Fact]
    public async Task ItemPicked_ZeroPick_AppendsDiscrepancy_NoStockPicked()
    {
        var sku = "PICK-ZERO-001";
        var warehouseId = "NJ-FC";
        var (inventoryId, reservationId, orderId) = await SeedCommittedInventory(sku, warehouseId, 100, 10);

        // Picker found nothing
        await _fixture.ExecuteAndWaitAsync(
            new ItemPicked(orderId, sku, warehouseId, 0, DateTimeOffset.UtcNow));

        await using var session = _fixture.GetDocumentSession();
        var inventory = await session.LoadAsync<ProductInventory>(inventoryId);

        inventory.ShouldNotBeNull();
        // No StockPicked — nothing was physically removed
        inventory.PickedAllocations.ShouldNotContainKey(reservationId);
        // Committed allocation remains
        inventory.CommittedAllocations.ShouldContainKey(reservationId);

        var events = await session.Events.FetchStreamAsync(inventoryId);
        events.ShouldContain(e => e.EventType == typeof(StockDiscrepancyFound));
        events.ShouldNotContain(e => e.EventType == typeof(StockPicked));
    }

    [Fact]
    public async Task ItemPicked_ReleasedReservation_IsNoOp()
    {
        var sku = "PICK-RELEASED-001";
        var warehouseId = "NJ-FC";
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, warehouseId, 100));

        var inventoryId = InventoryStreamId.Compute(sku, warehouseId);
        var orderId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();

        // Reserve, then release (simulate cancellation)
        await _fixture.ExecuteAndWaitAsync(new ReserveStock(orderId, sku, warehouseId, reservationId, 20));
        await _fixture.ExecuteAndWaitAsync(new ReleaseReservation(inventoryId, reservationId, "cancelled"));

        // Stale ItemPicked arrives after release
        await _fixture.ExecuteAndWaitAsync(
            new ItemPicked(orderId, sku, warehouseId, 20, DateTimeOffset.UtcNow));

        await using var session = _fixture.GetDocumentSession();
        var inventory = await session.LoadAsync<ProductInventory>(inventoryId);

        inventory.ShouldNotBeNull();
        inventory.PickedAllocations.ShouldBeEmpty();
        inventory.AvailableQuantity.ShouldBe(100); // fully restored
    }

    // ---------------------------------------------------------------------------
    // Slice 14: ShipmentHandedToCarrier → StockShipped
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ShipmentHandedToCarrier_AfterPick_AppendsStockShipped()
    {
        var sku = "SHIP-NORMAL-001";
        var warehouseId = "NJ-FC";
        var (inventoryId, reservationId, orderId) = await SeedCommittedInventory(sku, warehouseId, 100, 20);

        // Pick, then ship
        await _fixture.ExecuteAndWaitAsync(
            new ItemPicked(orderId, sku, warehouseId, 20, DateTimeOffset.UtcNow));

        var shipmentId = Guid.NewGuid();
        await _fixture.ExecuteAndWaitAsync(
            new ShipmentHandedToCarrier(orderId, shipmentId, "FedEx", "1Z999", DateTimeOffset.UtcNow));

        await using var session = _fixture.GetDocumentSession();
        var inventory = await session.LoadAsync<ProductInventory>(inventoryId);

        inventory.ShouldNotBeNull();
        inventory.PickedAllocations.ShouldBeEmpty();
        inventory.TotalOnHand.ShouldBe(80); // 100 - 20 shipped
        inventory.AvailableQuantity.ShouldBe(80);
    }

    [Fact]
    public async Task ShipmentHandedToCarrier_BeforePick_CombinedPickAndShip()
    {
        var sku = "SHIP-OOO-001";
        var warehouseId = "NJ-FC";
        var (inventoryId, reservationId, orderId) = await SeedCommittedInventory(sku, warehouseId, 100, 15);

        // Ship arrives BEFORE pick (out-of-order delivery)
        var shipmentId = Guid.NewGuid();
        await _fixture.ExecuteAndWaitAsync(
            new ShipmentHandedToCarrier(orderId, shipmentId, "UPS", "1Z888", DateTimeOffset.UtcNow));

        await using var session = _fixture.GetDocumentSession();
        var inventory = await session.LoadAsync<ProductInventory>(inventoryId);

        inventory.ShouldNotBeNull();
        inventory.CommittedAllocations.ShouldBeEmpty();
        inventory.PickedAllocations.ShouldBeEmpty();
        inventory.TotalOnHand.ShouldBe(85); // 100 - 15 shipped
        inventory.AvailableQuantity.ShouldBe(85);

        // Verify both events appended atomically
        var events = await session.Events.FetchStreamAsync(inventoryId);
        events.ShouldContain(e => e.EventType == typeof(StockPicked));
        events.ShouldContain(e => e.EventType == typeof(StockShipped));
    }

    [Fact]
    public async Task ShipmentHandedToCarrier_AlreadyShipped_IsNoOp()
    {
        var sku = "SHIP-IDEM-001";
        var warehouseId = "NJ-FC";
        var (inventoryId, reservationId, orderId) = await SeedCommittedInventory(sku, warehouseId, 100, 10);

        // Pick, ship, then duplicate ship
        await _fixture.ExecuteAndWaitAsync(
            new ItemPicked(orderId, sku, warehouseId, 10, DateTimeOffset.UtcNow));
        await _fixture.ExecuteAndWaitAsync(
            new ShipmentHandedToCarrier(orderId, Guid.NewGuid(), "FedEx", "1Z111", DateTimeOffset.UtcNow));

        var totalAfterShip = 90;

        // Duplicate ShipmentHandedToCarrier — should be no-op
        await _fixture.ExecuteAndWaitAsync(
            new ShipmentHandedToCarrier(orderId, Guid.NewGuid(), "FedEx", "1Z111", DateTimeOffset.UtcNow));

        await using var session = _fixture.GetDocumentSession();
        var inventory = await session.LoadAsync<ProductInventory>(inventoryId);

        inventory.ShouldNotBeNull();
        inventory.TotalOnHand.ShouldBe(totalAfterShip); // unchanged from first ship
    }
}
