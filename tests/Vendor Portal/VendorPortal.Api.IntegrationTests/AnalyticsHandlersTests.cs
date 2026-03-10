using Marten;
using Messages.Contracts.Inventory;
using Messages.Contracts.Orders;
using Shouldly;
using VendorPortal.Analytics;
using VendorPortal.VendorProductCatalog;

namespace VendorPortal.Api.IntegrationTests;

/// <summary>
/// Integration tests for Phase 3 analytics handlers:
/// verifies that Inventory and Order integration events are correctly handled,
/// persisted as Marten documents, and would produce the right SignalR hub messages.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class AnalyticsHandlersTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public AnalyticsHandlersTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // ───────────────────────────────────────────────
    // LowStockDetected handler tests
    // ───────────────────────────────────────────────

    [Fact]
    public async Task LowStockDetected_CreatesLowStockAlert_WhenSkuIsAssigned()
    {
        // Arrange — seed VendorProductCatalog entry so the handler knows the tenant
        var vendorId = Guid.NewGuid();
        await SeedCatalogEntry("CAT-FOOD-001", vendorId);

        var @event = new LowStockDetected(
            Sku: "CAT-FOOD-001",
            WarehouseId: "WH-EAST",
            CurrentQuantity: 3,
            ThresholdQuantity: 10,
            DetectedAt: DateTimeOffset.UtcNow);

        // Act
        await _fixture.ExecuteMessageAsync(@event);

        // Assert — alert document was created
        using var session = _fixture.GetDocumentSession();
        var alertId = LowStockAlert.BuildId(vendorId, "CAT-FOOD-001");
        var alert = await session.LoadAsync<LowStockAlert>(alertId);

        alert.ShouldNotBeNull();
        alert.VendorTenantId.ShouldBe(vendorId);
        alert.Sku.ShouldBe("CAT-FOOD-001");
        alert.WarehouseId.ShouldBe("WH-EAST");
        alert.CurrentQuantity.ShouldBe(3);
        alert.ThresholdQuantity.ShouldBe(10);
        alert.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task LowStockDetected_UpdatesExistingAlert_WithoutCreatingDuplicate()
    {
        // Arrange — seed catalog entry and fire the first alert
        var vendorId = Guid.NewGuid();
        await SeedCatalogEntry("DOG-FOOD-001", vendorId);

        var firstEvent = new LowStockDetected("DOG-FOOD-001", "WH-EAST", 3, 10, DateTimeOffset.UtcNow.AddMinutes(-5));
        await _fixture.ExecuteMessageAsync(firstEvent);

        // Act — fire a second alert for the same SKU (lower quantity)
        var secondEvent = new LowStockDetected("DOG-FOOD-001", "WH-EAST", 1, 10, DateTimeOffset.UtcNow);
        await _fixture.ExecuteMessageAsync(secondEvent);

        // Assert — still only one active alert (dedup), with updated quantity
        using var session = _fixture.GetDocumentSession();
        var alertId = LowStockAlert.BuildId(vendorId, "DOG-FOOD-001");
        var alert = await session.LoadAsync<LowStockAlert>(alertId);

        alert.ShouldNotBeNull();
        alert.CurrentQuantity.ShouldBe(1);
        alert.IsActive.ShouldBeTrue();
        // FirstDetectedAt should match the FIRST event, not the second
        alert.FirstDetectedAt.ShouldBeLessThan(alert.LastUpdatedAt);
    }

    [Fact]
    public async Task LowStockDetected_Skips_WhenSkuHasNoVendorAssignment()
    {
        // Arrange — no catalog entry for this SKU
        var @event = new LowStockDetected(
            Sku: "UNKNOWN-SKU-999",
            WarehouseId: "WH-EAST",
            CurrentQuantity: 0,
            ThresholdQuantity: 5,
            DetectedAt: DateTimeOffset.UtcNow);

        // Act — should not throw
        await _fixture.ExecuteMessageAsync(@event);

        // Assert — no alert document created
        using var session = _fixture.GetDocumentSession();
        var alertId = LowStockAlert.BuildId(Guid.Empty, "UNKNOWN-SKU-999");
        var alert = await session.LoadAsync<LowStockAlert>(alertId);
        alert.ShouldBeNull();
    }

    // ───────────────────────────────────────────────
    // InventoryAdjusted handler tests
    // ───────────────────────────────────────────────

    [Fact]
    public async Task InventoryAdjusted_CreatesInventorySnapshot_WhenSkuIsAssigned()
    {
        // Arrange
        var vendorId = Guid.NewGuid();
        await SeedCatalogEntry("BIRD-SEED-001", vendorId);

        var @event = new InventoryAdjusted(
            Sku: "BIRD-SEED-001",
            WarehouseId: "WH-WEST",
            QuantityChange: -5,
            NewQuantity: 45,
            AdjustedAt: DateTimeOffset.UtcNow);

        // Act
        await _fixture.ExecuteMessageAsync(@event);

        // Assert
        using var session = _fixture.GetDocumentSession();
        var snapshotId = InventorySnapshot.BuildId(vendorId, "BIRD-SEED-001", "WH-WEST");
        var snapshot = await session.LoadAsync<InventorySnapshot>(snapshotId);

        snapshot.ShouldNotBeNull();
        snapshot.VendorTenantId.ShouldBe(vendorId);
        snapshot.Sku.ShouldBe("BIRD-SEED-001");
        snapshot.WarehouseId.ShouldBe("WH-WEST");
        snapshot.CurrentQuantity.ShouldBe(45);
    }

    [Fact]
    public async Task InventoryAdjusted_UpdatesExistingSnapshot_OnSubsequentEvents()
    {
        // Arrange
        var vendorId = Guid.NewGuid();
        await SeedCatalogEntry("FISH-FOOD-001", vendorId);

        await _fixture.ExecuteMessageAsync(new InventoryAdjusted("FISH-FOOD-001", "WH-NORTH", -10, 90, DateTimeOffset.UtcNow.AddMinutes(-1)));

        // Act — subsequent adjustment
        await _fixture.ExecuteMessageAsync(new InventoryAdjusted("FISH-FOOD-001", "WH-NORTH", -5, 85, DateTimeOffset.UtcNow));

        // Assert — snapshot reflects latest quantity
        using var session = _fixture.GetDocumentSession();
        var snapshotId = InventorySnapshot.BuildId(vendorId, "FISH-FOOD-001", "WH-NORTH");
        var snapshot = await session.LoadAsync<InventorySnapshot>(snapshotId);

        snapshot.ShouldNotBeNull();
        snapshot.CurrentQuantity.ShouldBe(85);
    }

    [Fact]
    public async Task InventoryAdjusted_Skips_WhenSkuHasNoVendorAssignment()
    {
        // Act — should not throw
        await _fixture.ExecuteMessageAsync(new InventoryAdjusted("UNKNOWN-999", "WH-EAST", -1, 0, DateTimeOffset.UtcNow));

        // Assert — no snapshot created
        using var session = _fixture.GetDocumentSession();
        var allSnapshots = await session.Query<InventorySnapshot>().ToListAsync();
        allSnapshots.ShouldBeEmpty();
    }

    // ───────────────────────────────────────────────
    // StockReplenished handler tests
    // ───────────────────────────────────────────────

    [Fact]
    public async Task StockReplenished_UpdatesInventorySnapshot_WhenSkuIsAssigned()
    {
        // Arrange
        var vendorId = Guid.NewGuid();
        await SeedCatalogEntry("REPTILE-FOOD-001", vendorId);

        var @event = new StockReplenished(
            Sku: "REPTILE-FOOD-001",
            WarehouseId: "WH-SOUTH",
            QuantityAdded: 100,
            NewQuantity: 150,
            ReplenishedAt: DateTimeOffset.UtcNow);

        // Act
        await _fixture.ExecuteMessageAsync(@event);

        // Assert
        using var session = _fixture.GetDocumentSession();
        var snapshotId = InventorySnapshot.BuildId(vendorId, "REPTILE-FOOD-001", "WH-SOUTH");
        var snapshot = await session.LoadAsync<InventorySnapshot>(snapshotId);

        snapshot.ShouldNotBeNull();
        snapshot.CurrentQuantity.ShouldBe(150);
        snapshot.VendorTenantId.ShouldBe(vendorId);
    }

    // ───────────────────────────────────────────────
    // OrderPlaced analytics fan-out tests
    // ───────────────────────────────────────────────

    [Fact]
    public async Task OrderPlaced_DoesNotThrow_WhenSkusHaveNoVendorAssignment()
    {
        // Arrange — no catalog entries seeded
        var @event = new OrderPlaced(
            OrderId: Guid.NewGuid(),
            CustomerId: Guid.NewGuid(),
            LineItems: [new OrderLineItem("UNKNOWN-SKU", 2, 9.99m, 19.98m)],
            ShippingAddress: new Messages.Contracts.Orders.ShippingAddress(
                "123 Main St", null, "Anytown", "CA", "90210", "US"),
            ShippingMethod: "standard",
            PaymentMethodToken: "tok_test",
            TotalAmount: 19.98m,
            PlacedAt: DateTimeOffset.UtcNow);

        // Act — should not throw; unknown SKUs are silently skipped
        var exception = await Record.ExceptionAsync(() => _fixture.ExecuteMessageAsync(@event));
        exception.ShouldBeNull();
    }

    [Fact]
    public async Task OrderPlaced_ProcessesSuccessfully_WhenSkusAreAssigned()
    {
        // Arrange
        var vendorAId = Guid.NewGuid();
        var vendorBId = Guid.NewGuid();
        await SeedCatalogEntry("CAT-TOY-001", vendorAId);
        await SeedCatalogEntry("DOG-LEASH-001", vendorBId);

        var @event = new OrderPlaced(
            OrderId: Guid.NewGuid(),
            CustomerId: Guid.NewGuid(),
            LineItems:
            [
                new OrderLineItem("CAT-TOY-001", 1, 12.99m, 12.99m),
                new OrderLineItem("DOG-LEASH-001", 1, 24.99m, 24.99m),
            ],
            ShippingAddress: new Messages.Contracts.Orders.ShippingAddress(
                "456 Oak Ave", null, "Springfield", "IL", "62701", "US"),
            ShippingMethod: "express",
            PaymentMethodToken: "tok_test",
            TotalAmount: 37.98m,
            PlacedAt: DateTimeOffset.UtcNow);

        // Act — should fan out SalesMetricUpdated for both vendors without throwing
        var exception = await Record.ExceptionAsync(() => _fixture.ExecuteMessageAsync(@event));
        exception.ShouldBeNull();
    }

    // ───────────────────────────────────────────────
    // Missed alerts catch-up endpoint tests
    // ───────────────────────────────────────────────

    [Fact]
    public async Task GetActiveLowStockAlerts_Returns401_WhenUnauthorized()
    {
        // Act — no auth token
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/vendor-portal/analytics/alerts/low-stock");
            s.StatusCodeShouldBe(401);
        });

        result.ShouldNotBeNull();
    }

    // ───────────────────────────────────────────────
    // Helpers
    // ───────────────────────────────────────────────

    private async Task SeedCatalogEntry(string sku, Guid vendorTenantId)
    {
        using var session = _fixture.GetDocumentSession();
        session.Store(new VendorProductCatalogEntry
        {
            Id = sku,
            Sku = sku,
            VendorTenantId = vendorTenantId,
            AssociatedBy = "test-setup",
            AssociatedAt = DateTimeOffset.UtcNow,
            IsActive = true
        });
        await session.SaveChangesAsync();
    }
}
