using Inventory.Management;
using Marten;

namespace Inventory.Api.IntegrationTests.Management;

/// <summary>
/// Integration tests for commit and release flows in Inventory BC.
/// Tests converting soft reservations to hard allocations and releasing stock back to pool.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class CommitReleaseFlowTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public CommitReleaseFlowTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Integration test for committing a reservation.
    /// Reserves stock, commits it, verifies conversion to committed allocation.
    /// </summary>
    [Fact]
    public async Task CommitReservation_Converts_Soft_To_Hard_Allocation()
    {
        // Arrange: Initialize and reserve stock
        var sku = "SKU-COMMIT-001";
        var warehouseId = "WH-01";
        var initialQuantity = 100;
        var reservationId = Guid.NewGuid();

        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, warehouseId, initialQuantity));

        await using var session = _fixture.GetDocumentSession();
        var inventory = await session.Query<ProductInventory>()
            .FirstAsync(i => i.SKU == sku && i.WarehouseId == warehouseId);

        var orderId = Guid.NewGuid();
        await _fixture.ExecuteAndWaitAsync(new ReserveStock(orderId, sku, warehouseId, reservationId, 25));

        // Act: Commit reservation
        var commitCommand = new CommitReservation(inventory.Id, reservationId);
        await _fixture.ExecuteAndWaitAsync(commitCommand);

        // Assert: Verify reservation committed
        await using var querySession = _fixture.GetDocumentSession();
        var updatedInventory = await querySession.LoadAsync<ProductInventory>(inventory.Id);

        updatedInventory.ShouldNotBeNull();
        updatedInventory.AvailableQuantity.ShouldBe(75); // Still 75 (no change from commit)
        updatedInventory.ReservedQuantity.ShouldBe(0); // Reservation removed
        updatedInventory.CommittedQuantity.ShouldBe(25); // Now committed
        updatedInventory.Reservations.ShouldNotContainKey(reservationId);
        updatedInventory.CommittedAllocations.ShouldContainKey(reservationId);
        updatedInventory.CommittedAllocations[reservationId].ShouldBe(25);
    }

    /// <summary>
    /// Integration test for releasing a reservation.
    /// Reserves stock, releases it, verifies stock returned to available pool.
    /// </summary>
    [Fact]
    public async Task ReleaseReservation_Returns_Stock_To_Available_Pool()
    {
        // Arrange: Initialize and reserve stock
        var sku = "SKU-RELEASE-001";
        var warehouseId = "WH-01";
        var initialQuantity = 100;
        var reservationId = Guid.NewGuid();

        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, warehouseId, initialQuantity));

        await using var session = _fixture.GetDocumentSession();
        var inventory = await session.Query<ProductInventory>()
            .FirstAsync(i => i.SKU == sku && i.WarehouseId == warehouseId);

        var orderId = Guid.NewGuid();
        await _fixture.ExecuteAndWaitAsync(new ReserveStock(orderId, sku, warehouseId, reservationId, 30));

        // Verify reservation exists
        await using var verifySession = _fixture.GetDocumentSession();
        var reservedInventory = await verifySession.LoadAsync<ProductInventory>(inventory.Id);
        reservedInventory!.ReservedQuantity.ShouldBe(30);
        reservedInventory.AvailableQuantity.ShouldBe(70);

        // Act: Release reservation
        var releaseCommand = new ReleaseReservation(inventory.Id, reservationId, "Order cancelled");
        await _fixture.ExecuteAndWaitAsync(releaseCommand);

        // Assert: Verify stock returned
        await using var querySession = _fixture.GetDocumentSession();
        var updatedInventory = await querySession.LoadAsync<ProductInventory>(inventory.Id);

        updatedInventory.ShouldNotBeNull();
        updatedInventory.AvailableQuantity.ShouldBe(100); // Back to full
        updatedInventory.ReservedQuantity.ShouldBe(0); // Reservation removed
        updatedInventory.CommittedQuantity.ShouldBe(0);
        updatedInventory.Reservations.ShouldBeEmpty();
    }

    /// <summary>
    /// Integration test for commit and release on multiple reservations.
    /// Reserves 3 times, commits 1, releases 1, verifies final state.
    /// </summary>
    [Fact]
    public async Task Multiple_Reservations_With_Commit_And_Release_Produces_Correct_State()
    {
        // Arrange: Initialize inventory
        var sku = "SKU-MULTI-001";
        var warehouseId = "WH-01";
        var initialQuantity = 100;

        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, warehouseId, initialQuantity));

        await using var session = _fixture.GetDocumentSession();
        var inventory = await session.Query<ProductInventory>()
            .FirstAsync(i => i.SKU == sku && i.WarehouseId == warehouseId);

        // Reserve three times
        var reservation1 = Guid.NewGuid();
        var reservation2 = Guid.NewGuid();
        var reservation3 = Guid.NewGuid();

        var orderId = Guid.NewGuid();
        await _fixture.ExecuteAndWaitAsync(new ReserveStock(orderId, sku, warehouseId, reservation1, 10));
        await _fixture.ExecuteAndWaitAsync(new ReserveStock(orderId, sku, warehouseId, reservation2, 20));
        await _fixture.ExecuteAndWaitAsync(new ReserveStock(orderId, sku, warehouseId, reservation3, 30));

        // Act: Commit reservation1, release reservation2, leave reservation3 as-is
        await _fixture.ExecuteAndWaitAsync(new CommitReservation(inventory.Id, reservation1));
        await _fixture.ExecuteAndWaitAsync(new ReleaseReservation(inventory.Id, reservation2, "Payment failed"));

        // Assert: Verify final state
        await using var querySession = _fixture.GetDocumentSession();
        var updatedInventory = await querySession.LoadAsync<ProductInventory>(inventory.Id);

        updatedInventory.ShouldNotBeNull();
        updatedInventory.AvailableQuantity.ShouldBe(60); // 100 - 10 (committed) - 30 (reserved)
        updatedInventory.ReservedQuantity.ShouldBe(30); // Only reservation3
        updatedInventory.CommittedQuantity.ShouldBe(10); // reservation1
        updatedInventory.TotalOnHand.ShouldBe(100); // 60 + 30 + 10

        updatedInventory.Reservations.Count.ShouldBe(1);
        updatedInventory.Reservations.ShouldContainKey(reservation3);

        updatedInventory.CommittedAllocations.Count.ShouldBe(1);
        updatedInventory.CommittedAllocations.ShouldContainKey(reservation1);
    }

    /// <summary>
    /// Integration test for receiving new stock.
    /// Initializes inventory, receives additional stock, verifies quantity increased.
    /// </summary>
    [Fact]
    public async Task ReceiveStock_Increases_Available_Quantity()
    {
        // Arrange: Initialize inventory
        var sku = "SKU-RECEIVE-001";
        var warehouseId = "WH-01";
        var initialQuantity = 50;

        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, warehouseId, initialQuantity));

        await using var session = _fixture.GetDocumentSession();
        var inventory = await session.Query<ProductInventory>()
            .FirstAsync(i => i.SKU == sku && i.WarehouseId == warehouseId);

        // Act: Receive new stock
        var receiveCommand = new ReceiveStock(inventory.Id, 100, "Supplier ABC");
        await _fixture.ExecuteAndWaitAsync(receiveCommand);

        // Assert: Verify quantity increased
        await using var querySession = _fixture.GetDocumentSession();
        var updatedInventory = await querySession.LoadAsync<ProductInventory>(inventory.Id);

        updatedInventory.ShouldNotBeNull();
        updatedInventory.AvailableQuantity.ShouldBe(150); // 50 + 100
        updatedInventory.TotalOnHand.ShouldBe(150);
    }
}
