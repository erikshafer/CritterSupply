using Inventory.Management;
using Messages.Contracts.Fulfillment;

namespace Inventory.Api.IntegrationTests.Management;

/// <summary>
/// Integration tests for <see cref="StockReservationRequestedHandler"/> which handles
/// inbound <see cref="StockReservationRequested"/> messages from the Fulfillment BC.
/// Verifies the routing-aware reservation flow (warehouse-specific stock reservation).
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
        await _fixture.ExecuteAndWaitAsync(message);

        // Assert: inventory decremented and reservation tracked
        await using var session = _fixture.GetDocumentSession();
        var inventory = await session.LoadAsync<ProductInventory>(inventoryId);

        inventory.ShouldNotBeNull();
        inventory.AvailableQuantity.ShouldBe(60); // 100 - 40
        inventory.ReservedQuantity.ShouldBe(40);
        inventory.Reservations.ShouldContainKey(reservationId);
        inventory.Reservations[reservationId].ShouldBe(40);
        inventory.TotalOnHand.ShouldBe(100); // conservation invariant
    }

    [Fact]
    public async Task Insufficient_Stock_Does_Not_Apply_Reservation()
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
        await _fixture.ExecuteAndWaitAsync(message);

        // Assert: inventory unchanged — reservation rejected by Before() validation
        await using var session = _fixture.GetDocumentSession();
        var inventory = await session.LoadAsync<ProductInventory>(inventoryId);

        inventory.ShouldNotBeNull();
        inventory.AvailableQuantity.ShouldBe(initialQuantity); // unchanged
        inventory.ReservedQuantity.ShouldBe(0);
        inventory.Reservations.ShouldBeEmpty();
    }
}
