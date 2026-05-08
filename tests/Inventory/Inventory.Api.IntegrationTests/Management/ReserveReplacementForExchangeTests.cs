using Inventory.Management;
using IntegrationContracts = Messages.Contracts.Inventory;

namespace Inventory.Api.IntegrationTests.Management;

/// <summary>
/// Integration tests for <see cref="ReserveReplacementForExchangeHandler"/>,
/// the M47.0 / Slice 1 entry point that lets the Returns BC place a hold
/// on a replacement SKU for a cross-product exchange. Mirrors the
/// shape of <see cref="StockReservationRequestedTests"/> but uses
/// <see cref="IntegrationContracts.ReserveReplacementForExchange"/> as
/// the trigger and verifies the new
/// <see cref="IntegrationContracts.ReplacementReserved"/> /
/// <see cref="IntegrationContracts.ReplacementReservationFailed"/>
/// outbound contracts. See ADR 0061.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class ReserveReplacementForExchangeTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public ReserveReplacementForExchangeTests(TestFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Sufficient_Stock_Reserves_And_Publishes_ReplacementReserved()
    {
        // Arrange: initialize replacement SKU at the warehouse Returns
        // will target by default (WH-01 per ADR 0061).
        var sku = "REPL-HAPPY-001";
        var warehouseId = "WH-01";
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, warehouseId, 100));

        var inventoryId = InventoryStreamId.Compute(sku, warehouseId);
        var returnId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var requestedAt = DateTimeOffset.UtcNow;

        var message = new IntegrationContracts.ReserveReplacementForExchange(
            ReturnId: returnId,
            OrderId: orderId,
            CustomerId: customerId,
            ReplacementSku: sku,
            WarehouseId: warehouseId,
            Quantity: 3,
            RequestedAt: requestedAt);

        // Act
        var tracked = await _fixture.ExecuteAndWaitAsync(message);

        // Assert: aggregate state — reservation is keyed by ReturnId
        // (per ADR 0061), available decremented, conservation holds.
        await using var session = _fixture.GetDocumentSession();
        var inventory = await session.LoadAsync<ProductInventory>(inventoryId);
        inventory.ShouldNotBeNull();
        inventory.AvailableQuantity.ShouldBe(97);
        inventory.ReservedQuantity.ShouldBe(3);
        inventory.Reservations.ShouldContainKey(returnId);
        inventory.Reservations[returnId].ShouldBe(3);
        inventory.ReservationOrderIds.ShouldContainKey(returnId);
        inventory.ReservationOrderIds[returnId].ShouldBe(orderId);
        inventory.TotalOnHand.ShouldBe(100); // conservation invariant

        // Assert: outbound contract
        var reserved = tracked.Sent.SingleMessage<IntegrationContracts.ReplacementReserved>();
        reserved.ReturnId.ShouldBe(returnId);
        reserved.OrderId.ShouldBe(orderId);
        reserved.InventoryId.ShouldBe(inventoryId);
        reserved.Sku.ShouldBe(sku);
        reserved.WarehouseId.ShouldBe(warehouseId);
        reserved.Quantity.ShouldBe(3);

        // No failure published on the happy path.
        tracked.Sent.MessagesOf<IntegrationContracts.ReplacementReservationFailed>().ShouldBeEmpty();
    }

    [Fact]
    public async Task Insufficient_Stock_Publishes_ReplacementReservationFailed_Without_Mutating_Aggregate()
    {
        // Arrange: replacement SKU with limited stock.
        var sku = "REPL-LOW-001";
        var warehouseId = "WH-01";
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, warehouseId, 2));

        var inventoryId = InventoryStreamId.Compute(sku, warehouseId);
        var returnId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var message = new IntegrationContracts.ReserveReplacementForExchange(
            ReturnId: returnId,
            OrderId: orderId,
            CustomerId: Guid.NewGuid(),
            ReplacementSku: sku,
            WarehouseId: warehouseId,
            Quantity: 5, // more than available
            RequestedAt: DateTimeOffset.UtcNow);

        // Act
        var tracked = await _fixture.ExecuteAndWaitAsync(message);

        // Assert: aggregate untouched.
        await using var session = _fixture.GetDocumentSession();
        var inventory = await session.LoadAsync<ProductInventory>(inventoryId);
        inventory.ShouldNotBeNull();
        inventory.AvailableQuantity.ShouldBe(2);
        inventory.ReservedQuantity.ShouldBe(0);
        inventory.Reservations.ShouldNotContainKey(returnId);

        // Assert: outbound failure published.
        var failed = tracked.Sent.SingleMessage<IntegrationContracts.ReplacementReservationFailed>();
        failed.ReturnId.ShouldBe(returnId);
        failed.OrderId.ShouldBe(orderId);
        failed.Sku.ShouldBe(sku);
        failed.WarehouseId.ShouldBe(warehouseId);
        failed.RequestedQuantity.ShouldBe(5);
        failed.AvailableQuantity.ShouldBe(2);
        failed.Reason.ShouldContain("Insufficient stock");

        tracked.Sent.MessagesOf<IntegrationContracts.ReplacementReserved>().ShouldBeEmpty();
    }

    [Fact]
    public async Task Missing_Inventory_Publishes_ReplacementReservationFailed()
    {
        // Arrange: do NOT initialize inventory for this SKU+warehouse.
        var sku = "REPL-MISSING-001";
        var warehouseId = "WH-01";
        var returnId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var message = new IntegrationContracts.ReserveReplacementForExchange(
            ReturnId: returnId,
            OrderId: orderId,
            CustomerId: Guid.NewGuid(),
            ReplacementSku: sku,
            WarehouseId: warehouseId,
            Quantity: 1,
            RequestedAt: DateTimeOffset.UtcNow);

        // Act
        var tracked = await _fixture.ExecuteAndWaitAsync(message);

        // Assert: failure published with AvailableQuantity = 0 and a
        // reason that names the SKU + warehouse.
        var failed = tracked.Sent.SingleMessage<IntegrationContracts.ReplacementReservationFailed>();
        failed.ReturnId.ShouldBe(returnId);
        failed.AvailableQuantity.ShouldBe(0);
        failed.Reason.ShouldContain(sku);
        failed.Reason.ShouldContain(warehouseId);
    }

    [Fact]
    public async Task Duplicate_Delivery_Is_Idempotent_On_ReturnId()
    {
        // Arrange: a successful first reservation.
        var sku = "REPL-IDEMP-001";
        var warehouseId = "WH-01";
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, warehouseId, 50));

        var inventoryId = InventoryStreamId.Compute(sku, warehouseId);
        var returnId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var message = new IntegrationContracts.ReserveReplacementForExchange(
            ReturnId: returnId,
            OrderId: orderId,
            CustomerId: Guid.NewGuid(),
            ReplacementSku: sku,
            WarehouseId: warehouseId,
            Quantity: 4,
            RequestedAt: DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(message);

        // Act: re-deliver the same message (at-least-once semantics).
        var tracked = await _fixture.ExecuteAndWaitAsync(message);

        // Assert: aggregate state did not double-reserve.
        await using var session = _fixture.GetDocumentSession();
        var inventory = await session.LoadAsync<ProductInventory>(inventoryId);
        inventory.ShouldNotBeNull();
        inventory.AvailableQuantity.ShouldBe(46); // 50 - 4 (not 50 - 8)
        inventory.ReservedQuantity.ShouldBe(4);
        inventory.Reservations[returnId].ShouldBe(4);

        // Assert: re-delivery still emits a confirmation (so Returns
        // does not get stranded if its inbox lost the first reply),
        // but no failure.
        var reserved = tracked.Sent.SingleMessage<IntegrationContracts.ReplacementReserved>();
        reserved.ReturnId.ShouldBe(returnId);
        reserved.Quantity.ShouldBe(4);
        tracked.Sent.MessagesOf<IntegrationContracts.ReplacementReservationFailed>().ShouldBeEmpty();
    }
}
