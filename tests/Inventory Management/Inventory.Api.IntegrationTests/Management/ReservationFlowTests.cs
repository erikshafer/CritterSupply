using Inventory.Management;
using Marten;

namespace Inventory.Api.IntegrationTests.Management;

/// <summary>
/// Integration tests for reservation flow in Inventory BC.
/// Tests the complete flow from InitializeInventory through ReserveStock.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class ReservationFlowTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public ReservationFlowTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Integration test for successful stock reservation.
    /// Initializes inventory, reserves stock, verifies reservation confirmed.
    /// </summary>
    [Fact]
    public async Task ReserveStock_With_Sufficient_Quantity_Succeeds()
    {
        // Arrange: Initialize inventory
        var sku = "SKU-TEST-001";
        var warehouseId = "WH-01";
        var initialQuantity = 100;

        var initCommand = new InitializeInventory(sku, warehouseId, initialQuantity);
        await _fixture.ExecuteAndWaitAsync(initCommand);

        // Verify inventory created
        await using var session = _fixture.GetDocumentSession();
        var inventory = await session.Query<ProductInventory>()
            .FirstAsync(i => i.Sku == sku && i.WarehouseId == warehouseId);

        inventory.AvailableQuantity.ShouldBe(initialQuantity);

        // Act: Reserve stock
        var reservationId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var reserveCommand = new ReserveStock(orderId, sku, warehouseId, reservationId, 25);
        await _fixture.ExecuteAndWaitAsync(reserveCommand);

        // Assert: Verify reservation succeeded
        await using var querySession = _fixture.GetDocumentSession();
        var updatedInventory = await querySession.LoadAsync<ProductInventory>(inventory.Id);

        updatedInventory.ShouldNotBeNull();
        updatedInventory.AvailableQuantity.ShouldBe(75); // 100 - 25
        updatedInventory.ReservedQuantity.ShouldBe(25);
        updatedInventory.Reservations.ShouldContainKey(reservationId);
        updatedInventory.Reservations[reservationId].ShouldBe(25);
    }

    /// <summary>
    /// Integration test for insufficient stock scenario.
    /// Attempts to reserve more than available, expects failure.
    /// </summary>
    [Fact]
    public async Task ReserveStock_With_Insufficient_Quantity_Fails()
    {
        // Arrange: Initialize inventory with limited stock
        var sku = "SKU-TEST-002";
        var warehouseId = "WH-01";
        var initialQuantity = 10;

        var initCommand = new InitializeInventory(sku, warehouseId, initialQuantity);
        await _fixture.ExecuteAndWaitAsync(initCommand);

        // Act: Attempt to reserve more than available
        var reservationId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var reserveCommand = new ReserveStock(orderId, sku, warehouseId, reservationId, 50);

        // The handler will fail validation in Before() method, but ExecuteAndWaitAsync doesn't throw
        // We need to verify the inventory state hasn't changed
        await _fixture.ExecuteAndWaitAsync(reserveCommand);

        // Assert: Verify reservation did NOT occur
        await using var querySession = _fixture.GetDocumentSession();
        var inventory = await querySession.Query<ProductInventory>()
            .FirstAsync(i => i.Sku == sku && i.WarehouseId == warehouseId);

        inventory.AvailableQuantity.ShouldBe(initialQuantity); // Unchanged
        inventory.ReservedQuantity.ShouldBe(0); // No reservation
        inventory.Reservations.ShouldBeEmpty();
    }

    /// <summary>
    /// Integration test for multiple reservations on same inventory.
    /// Verifies multiple orders can reserve from same SKU.
    /// </summary>
    [Fact]
    public async Task ReserveStock_Multiple_Reservations_On_Same_SKU_Succeeds()
    {
        // Arrange: Initialize inventory
        var sku = "SKU-TEST-003";
        var warehouseId = "WH-01";
        var initialQuantity = 100;

        var initCommand = new InitializeInventory(sku, warehouseId, initialQuantity);
        await _fixture.ExecuteAndWaitAsync(initCommand);

        // Act: Reserve stock three times
        var reservation1 = Guid.NewGuid();
        var reservation2 = Guid.NewGuid();
        var reservation3 = Guid.NewGuid();

        var orderId = Guid.NewGuid();
        await _fixture.ExecuteAndWaitAsync(new ReserveStock(orderId, sku, warehouseId, reservation1, 10));
        await _fixture.ExecuteAndWaitAsync(new ReserveStock(orderId, sku, warehouseId, reservation2, 20));
        await _fixture.ExecuteAndWaitAsync(new ReserveStock(orderId, sku, warehouseId, reservation3, 30));

        // Assert: Verify all reservations succeeded
        await using var querySession = _fixture.GetDocumentSession();
        var inventory = await querySession.Query<ProductInventory>()
            .FirstAsync(i => i.Sku == sku && i.WarehouseId == warehouseId);

        inventory.AvailableQuantity.ShouldBe(40); // 100 - 10 - 20 - 30
        inventory.ReservedQuantity.ShouldBe(60); // 10 + 20 + 30
        inventory.Reservations.Count.ShouldBe(3);
        inventory.Reservations[reservation1].ShouldBe(10);
        inventory.Reservations[reservation2].ShouldBe(20);
        inventory.Reservations[reservation3].ShouldBe(30);
    }

    /// <summary>
    /// Integration test for reserving exact available quantity.
    /// Verifies edge case where available = requested.
    /// </summary>
    [Fact]
    public async Task ReserveStock_Exact_Available_Quantity_Succeeds()
    {
        // Arrange: Initialize inventory
        var sku = "SKU-TEST-004";
        var warehouseId = "WH-01";
        var initialQuantity = 50;

        var initCommand = new InitializeInventory(sku, warehouseId, initialQuantity);
        await _fixture.ExecuteAndWaitAsync(initCommand);

        // Act: Reserve exact available quantity
        var reservationId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var reserveCommand = new ReserveStock(orderId, sku, warehouseId, reservationId, 50);
        await _fixture.ExecuteAndWaitAsync(reserveCommand);

        // Assert: Verify reservation succeeded and available = 0
        await using var querySession = _fixture.GetDocumentSession();
        var inventory = await querySession.Query<ProductInventory>()
            .FirstAsync(i => i.Sku == sku && i.WarehouseId == warehouseId);

        inventory.AvailableQuantity.ShouldBe(0);
        inventory.ReservedQuantity.ShouldBe(50);
        inventory.TotalOnHand.ShouldBe(50);
    }
}
