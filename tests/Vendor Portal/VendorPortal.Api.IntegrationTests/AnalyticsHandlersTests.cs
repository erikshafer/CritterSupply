using Marten;
using Messages.Contracts.Inventory;
using Messages.Contracts.Orders;
using Shouldly;
using VendorPortal.Analytics;
using VendorPortal.RealTime;
using VendorPortal.VendorProductCatalog;

namespace VendorPortal.Api.IntegrationTests;

/// <summary>
/// Integration tests for Phase 3 analytics handlers:
/// verifies that Inventory and Order integration events are correctly handled,
/// persisted as Marten documents, and produce the correct SignalR hub messages.
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
    // LowStockDetected handler — persistence tests
    // ───────────────────────────────────────────────

    [Fact]
    public async Task LowStockDetected_CreatesLowStockAlert_WhenSkuIsAssigned()
    {
        // Arrange
        var vendorId = Guid.NewGuid();
        await SeedCatalogEntry("CAT-FOOD-001", vendorId);

        var @event = new LowStockDetected("CAT-FOOD-001", "WH-EAST", 3, 10, DateTimeOffset.UtcNow);

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
        var @event = new LowStockDetected("UNKNOWN-SKU-999", "WH-EAST", 0, 5, DateTimeOffset.UtcNow);

        // Act — should not throw
        await _fixture.ExecuteMessageAsync(@event);

        // Assert — no alert document created
        using var session = _fixture.GetDocumentSession();
        var allAlerts = await session.Query<LowStockAlert>().ToListAsync();
        allAlerts.ShouldBeEmpty();
    }

    [Fact]
    public async Task LowStockDetected_Skips_WhenCatalogEntryIsInactive()
    {
        // Arrange — inactive catalog entry (de-listed SKU)
        var vendorId = Guid.NewGuid();
        await SeedCatalogEntry("INACTIVE-SKU-001", vendorId, isActive: false);

        var @event = new LowStockDetected("INACTIVE-SKU-001", "WH-EAST", 0, 5, DateTimeOffset.UtcNow);

        // Act
        await _fixture.ExecuteMessageAsync(@event);

        // Assert — no alert created for inactive SKU
        using var session = _fixture.GetDocumentSession();
        var allAlerts = await session.Query<LowStockAlert>().ToListAsync();
        allAlerts.ShouldBeEmpty();
    }

    // ───────────────────────────────────────────────
    // LowStockDetected handler — hub message tests
    // ───────────────────────────────────────────────

    [Fact]
    public async Task LowStockDetected_PublishesLowStockAlertRaised_OnNewAlert()
    {
        // Arrange
        var vendorId = Guid.NewGuid();
        await SeedCatalogEntry("NEW-ALERT-SKU", vendorId);

        var @event = new LowStockDetected("NEW-ALERT-SKU", "WH-NORTH", 2, 15, DateTimeOffset.UtcNow);

        // Act
        var tracked = await _fixture.TrackMessageAsync(@event);

        // Assert — hub message was published with correct values
        var hubMsg = tracked.Sent.SingleMessage<LowStockAlertRaised>();
        hubMsg.VendorTenantId.ShouldBe(vendorId);
        hubMsg.Sku.ShouldBe("NEW-ALERT-SKU");
        hubMsg.WarehouseId.ShouldBe("WH-NORTH");
        hubMsg.CurrentQuantity.ShouldBe(2);
        hubMsg.ThresholdQuantity.ShouldBe(15);
    }

    [Fact]
    public async Task LowStockDetected_DoesNotPublishHubMessage_OnDuplicateAlert()
    {
        // Arrange — seed an existing alert
        var vendorId = Guid.NewGuid();
        await SeedCatalogEntry("DEDUP-SKU-001", vendorId);
        await _fixture.ExecuteMessageAsync(
            new LowStockDetected("DEDUP-SKU-001", "WH-EAST", 3, 10, DateTimeOffset.UtcNow.AddMinutes(-5)));

        // Act — fire a second alert for the same SKU
        var tracked = await _fixture.TrackMessageAsync(
            new LowStockDetected("DEDUP-SKU-001", "WH-EAST", 1, 10, DateTimeOffset.UtcNow));

        // Assert — NO hub push on duplicate (business rule: no noise on quantity updates)
        tracked.Sent.MessagesOf<LowStockAlertRaised>().ShouldBeEmpty();
    }

    // ───────────────────────────────────────────────
    // InventoryAdjusted handler — persistence + hub message tests
    // ───────────────────────────────────────────────

    [Fact]
    public async Task InventoryAdjusted_CreatesInventorySnapshot_WhenSkuIsAssigned()
    {
        // Arrange
        var vendorId = Guid.NewGuid();
        await SeedCatalogEntry("BIRD-SEED-001", vendorId);

        var @event = new InventoryAdjusted("BIRD-SEED-001", "WH-WEST", -5, 45, DateTimeOffset.UtcNow);

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
    public async Task InventoryAdjusted_CreatesDistinctSnapshots_PerWarehouse()
    {
        // Arrange — same SKU, two different warehouses
        var vendorId = Guid.NewGuid();
        await SeedCatalogEntry("MULTI-WH-SKU", vendorId);

        // Act — adjustments in two separate warehouses
        await _fixture.ExecuteMessageAsync(new InventoryAdjusted("MULTI-WH-SKU", "WH-EAST", -5, 45, DateTimeOffset.UtcNow));
        await _fixture.ExecuteMessageAsync(new InventoryAdjusted("MULTI-WH-SKU", "WH-WEST", -3, 97, DateTimeOffset.UtcNow));

        // Assert — two distinct snapshot documents (composite key includes WarehouseId)
        using var session = _fixture.GetDocumentSession();
        var eastId = InventorySnapshot.BuildId(vendorId, "MULTI-WH-SKU", "WH-EAST");
        var westId = InventorySnapshot.BuildId(vendorId, "MULTI-WH-SKU", "WH-WEST");

        var eastSnapshot = await session.LoadAsync<InventorySnapshot>(eastId);
        var westSnapshot = await session.LoadAsync<InventorySnapshot>(westId);

        eastSnapshot.ShouldNotBeNull();
        eastSnapshot.CurrentQuantity.ShouldBe(45);

        westSnapshot.ShouldNotBeNull();
        westSnapshot.CurrentQuantity.ShouldBe(97);
    }

    [Fact]
    public async Task InventoryAdjusted_PublishesInventoryLevelUpdated_WhenSkuIsAssigned()
    {
        // Arrange
        var vendorId = Guid.NewGuid();
        await SeedCatalogEntry("HUB-MSG-SKU", vendorId);

        // Act
        var tracked = await _fixture.TrackMessageAsync(
            new InventoryAdjusted("HUB-MSG-SKU", "WH-SOUTH", -2, 18, DateTimeOffset.UtcNow));

        // Assert — hub message published for each known-SKU adjustment
        var hubMsg = tracked.Sent.SingleMessage<InventoryLevelUpdated>();
        hubMsg.VendorTenantId.ShouldBe(vendorId);
        hubMsg.Sku.ShouldBe("HUB-MSG-SKU");
        hubMsg.WarehouseId.ShouldBe("WH-SOUTH");
        hubMsg.NewQuantity.ShouldBe(18);
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

        var @event = new StockReplenished("REPTILE-FOOD-001", "WH-SOUTH", 100, 150, DateTimeOffset.UtcNow);

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

    [Fact]
    public async Task StockReplenished_UpdatesExistingSnapshot_OnSubsequentReplenishment()
    {
        // Arrange — existing snapshot from initial adjustment
        var vendorId = Guid.NewGuid();
        await SeedCatalogEntry("HAMSTER-FOOD-001", vendorId);
        await _fixture.ExecuteMessageAsync(new StockReplenished("HAMSTER-FOOD-001", "WH-EAST", 50, 50, DateTimeOffset.UtcNow.AddMinutes(-30)));

        // Act — second replenishment
        await _fixture.ExecuteMessageAsync(new StockReplenished("HAMSTER-FOOD-001", "WH-EAST", 100, 150, DateTimeOffset.UtcNow));

        // Assert — snapshot updated, not duplicated
        using var session = _fixture.GetDocumentSession();
        var snapshotId = InventorySnapshot.BuildId(vendorId, "HAMSTER-FOOD-001", "WH-EAST");
        var snapshot = await session.LoadAsync<InventorySnapshot>(snapshotId);

        snapshot.ShouldNotBeNull();
        snapshot.CurrentQuantity.ShouldBe(150);
    }

    [Fact]
    public async Task StockReplenished_Skips_WhenSkuHasNoVendorAssignment()
    {
        // Act — should not throw
        await _fixture.ExecuteMessageAsync(new StockReplenished("UNKNOWN-REPLENISH-999", "WH-EAST", 100, 100, DateTimeOffset.UtcNow));

        // Assert — no snapshot created
        using var session = _fixture.GetDocumentSession();
        var allSnapshots = await session.Query<InventorySnapshot>().ToListAsync();
        allSnapshots.ShouldBeEmpty();
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
            ShippingAddress: new Messages.Contracts.Orders.ShippingAddress("123 Main St", null, "Anytown", "CA", "90210", "US"),
            ShippingMethod: "standard",
            PaymentMethodToken: "tok_test",
            TotalAmount: 19.98m,
            PlacedAt: DateTimeOffset.UtcNow);

        // Act — should not throw; unknown SKUs are silently skipped
        var exception = await Record.ExceptionAsync(() => _fixture.ExecuteMessageAsync(@event));
        exception.ShouldBeNull();
    }

    [Fact]
    public async Task OrderPlaced_FansOutSalesMetricUpdated_PerAffectedVendor()
    {
        // Arrange — two different vendors, each with one SKU in the order
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
            ShippingAddress: new Messages.Contracts.Orders.ShippingAddress("456 Oak Ave", null, "Springfield", "IL", "62701", "US"),
            ShippingMethod: "express",
            PaymentMethodToken: "tok_test",
            TotalAmount: 37.98m,
            PlacedAt: DateTimeOffset.UtcNow);

        // Act
        var tracked = await _fixture.TrackMessageAsync(@event);

        // Assert — one SalesMetricUpdated per vendor tenant (exactly 2)
        var hubMessages = tracked.Sent.MessagesOf<SalesMetricUpdated>().ToList();
        hubMessages.Count.ShouldBe(2);

        var tenantIds = hubMessages.Select(m => m.VendorTenantId).ToHashSet();
        tenantIds.ShouldContain(vendorAId);
        tenantIds.ShouldContain(vendorBId);
    }

    [Fact]
    public async Task OrderPlaced_FansOutExactlyOneMessage_WhenSameVendorHasMultipleLineItems()
    {
        // Arrange — same vendor owns both SKUs in the order
        var vendorId = Guid.NewGuid();
        await SeedCatalogEntry("BUNDLE-SKU-A", vendorId);
        await SeedCatalogEntry("BUNDLE-SKU-B", vendorId);

        var @event = new OrderPlaced(
            OrderId: Guid.NewGuid(),
            CustomerId: Guid.NewGuid(),
            LineItems:
            [
                new OrderLineItem("BUNDLE-SKU-A", 1, 5.99m, 5.99m),
                new OrderLineItem("BUNDLE-SKU-B", 2, 3.49m, 6.98m),
            ],
            ShippingAddress: new Messages.Contracts.Orders.ShippingAddress("789 Elm St", null, "Portland", "OR", "97201", "US"),
            ShippingMethod: "standard",
            PaymentMethodToken: "tok_test",
            TotalAmount: 12.97m,
            PlacedAt: DateTimeOffset.UtcNow);

        // Act
        var tracked = await _fixture.TrackMessageAsync(@event);

        // Assert — HashSet dedup: exactly ONE SalesMetricUpdated even though vendor has 2 line items
        var hubMessages = tracked.Sent.MessagesOf<SalesMetricUpdated>().ToList();
        hubMessages.Count.ShouldBe(1);
        hubMessages[0].VendorTenantId.ShouldBe(vendorId);
    }

    // ───────────────────────────────────────────────
    // GetActiveLowStockAlerts endpoint tests
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

    [Fact]
    public async Task GetActiveLowStockAlerts_Returns200WithAlerts_WhenAuthenticated()
    {
        // Arrange — seed a vendor and some alerts
        var vendorId = Guid.NewGuid();
        await SeedCatalogEntry("ALERT-SKU-001", vendorId);
        await SeedCatalogEntry("ALERT-SKU-002", vendorId);
        await _fixture.ExecuteMessageAsync(new LowStockDetected("ALERT-SKU-001", "WH-EAST", 2, 10, DateTimeOffset.UtcNow));
        await _fixture.ExecuteMessageAsync(new LowStockDetected("ALERT-SKU-002", "WH-EAST", 1, 5, DateTimeOffset.UtcNow));

        var token = _fixture.CreateTestJwt(vendorId);

        // Act
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/vendor-portal/analytics/alerts/low-stock");
            s.WithRequestHeader("Authorization", $"Bearer {token}");
            s.StatusCodeShouldBe(200);
        });

        var response = await result.ReadAsJsonAsync<ActiveLowStockAlertsResponse>();

        // Assert — both alerts returned
        response.ShouldNotBeNull();
        response.TotalCount.ShouldBe(2);
        response.Alerts.Count.ShouldBe(2);
        var skus = response.Alerts.Select(a => a.Sku).ToHashSet();
        skus.ShouldContain("ALERT-SKU-001");
        skus.ShouldContain("ALERT-SKU-002");
    }

    [Fact]
    public async Task GetActiveLowStockAlerts_ReturnsEmptyList_WhenNoAlerts()
    {
        // Arrange — authenticated vendor with no alerts
        var vendorId = Guid.NewGuid();
        var token = _fixture.CreateTestJwt(vendorId);

        // Act
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/vendor-portal/analytics/alerts/low-stock");
            s.WithRequestHeader("Authorization", $"Bearer {token}");
            s.StatusCodeShouldBe(200);
        });

        var response = await result.ReadAsJsonAsync<ActiveLowStockAlertsResponse>();

        response.ShouldNotBeNull();
        response.TotalCount.ShouldBe(0);
        response.Alerts.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetActiveLowStockAlerts_EnforcesTenantIsolation_ReturnsOnlyOwnAlerts()
    {
        // Arrange — vendor A and vendor B each have alerts
        var vendorAId = Guid.NewGuid();
        var vendorBId = Guid.NewGuid();

        await SeedCatalogEntry("VENDOR-A-SKU", vendorAId);
        await SeedCatalogEntry("VENDOR-B-SKU", vendorBId);
        await _fixture.ExecuteMessageAsync(new LowStockDetected("VENDOR-A-SKU", "WH-EAST", 1, 10, DateTimeOffset.UtcNow));
        await _fixture.ExecuteMessageAsync(new LowStockDetected("VENDOR-B-SKU", "WH-EAST", 2, 8, DateTimeOffset.UtcNow));

        // Act — vendor A authenticates and calls the endpoint
        var tokenA = _fixture.CreateTestJwt(vendorAId);
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/vendor-portal/analytics/alerts/low-stock");
            s.WithRequestHeader("Authorization", $"Bearer {tokenA}");
            s.StatusCodeShouldBe(200);
        });

        var response = await result.ReadAsJsonAsync<ActiveLowStockAlertsResponse>();

        // Assert — vendor A sees ONLY their own alerts
        response.ShouldNotBeNull();
        response.TotalCount.ShouldBe(1);
        response.Alerts[0].Sku.ShouldBe("VENDOR-A-SKU");
    }

    [Fact]
    public async Task GetActiveLowStockAlerts_FiltersBySince_ReturnsOnlyRecentAlerts()
    {
        // Arrange — vendor has alerts: one old, two recent
        var vendorId = Guid.NewGuid();
        await SeedCatalogEntry("RECENT-SKU-1", vendorId);
        await SeedCatalogEntry("RECENT-SKU-2", vendorId);
        await SeedCatalogEntry("OLD-SKU", vendorId);

        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-10);

        // Directly seed an "old" alert (before cutoff)
        using (var session = _fixture.GetDocumentSession())
        {
            session.Store(new LowStockAlert
            {
                Id = LowStockAlert.BuildId(vendorId, "OLD-SKU"),
                VendorTenantId = vendorId,
                Sku = "OLD-SKU",
                WarehouseId = "WH-EAST",
                CurrentQuantity = 1,
                ThresholdQuantity = 5,
                FirstDetectedAt = cutoff.AddMinutes(-20),
                LastUpdatedAt = cutoff.AddMinutes(-15),
                IsActive = true
            });
            await session.SaveChangesAsync();
        }

        // Seed two recent alerts (after cutoff)
        await _fixture.ExecuteMessageAsync(new LowStockDetected("RECENT-SKU-1", "WH-EAST", 2, 10, DateTimeOffset.UtcNow));
        await _fixture.ExecuteMessageAsync(new LowStockDetected("RECENT-SKU-2", "WH-EAST", 1, 5, DateTimeOffset.UtcNow));

        var token = _fixture.CreateTestJwt(vendorId);
        var sinceParam = Uri.EscapeDataString(cutoff.ToString("O"));

        // Act — request only alerts since the cutoff
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/vendor-portal/analytics/alerts/low-stock?since={sinceParam}");
            s.WithRequestHeader("Authorization", $"Bearer {token}");
            s.StatusCodeShouldBe(200);
        });

        var response = await result.ReadAsJsonAsync<ActiveLowStockAlertsResponse>();

        // Assert — only the two recent alerts returned; old alert excluded
        response.ShouldNotBeNull();
        response.TotalCount.ShouldBe(2);
        var skus = response.Alerts.Select(a => a.Sku).ToHashSet();
        skus.ShouldContain("RECENT-SKU-1");
        skus.ShouldContain("RECENT-SKU-2");
        skus.ShouldNotContain("OLD-SKU");
    }

    [Fact]
    public async Task GetActiveLowStockAlerts_Returns403_WhenVendorIsSuspended()
    {
        // Arrange — suspended vendor token
        var vendorId = Guid.NewGuid();
        var token = _fixture.CreateTestJwt(vendorId, tenantStatus: "Suspended");

        // Act
        await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/vendor-portal/analytics/alerts/low-stock");
            s.WithRequestHeader("Authorization", $"Bearer {token}");
            s.StatusCodeShouldBe(403);
        });
    }

    [Fact]
    public async Task GetActiveLowStockAlerts_Returns403_WhenVendorIsTerminated()
    {
        // Arrange — terminated vendor token (up to 15 min post-termination a valid JWT may still exist)
        var vendorId = Guid.NewGuid();
        var token = _fixture.CreateTestJwt(vendorId, tenantStatus: "Terminated");

        // Act
        await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/vendor-portal/analytics/alerts/low-stock");
            s.WithRequestHeader("Authorization", $"Bearer {token}");
            s.StatusCodeShouldBe(403);
        });
    }

    // ───────────────────────────────────────────────
    // Helpers
    // ───────────────────────────────────────────────

    private async Task SeedCatalogEntry(string sku, Guid vendorTenantId, bool isActive = true)
    {
        using var session = _fixture.GetDocumentSession();
        session.Store(new VendorProductCatalogEntry
        {
            Id = sku,
            Sku = sku,
            VendorTenantId = vendorTenantId,
            AssociatedBy = "test-setup",
            AssociatedAt = DateTimeOffset.UtcNow,
            IsActive = isActive
        });
        await session.SaveChangesAsync();
    }

    // Response DTOs matching the endpoint's anonymous record responses
    private sealed record ActiveLowStockAlertsResponse(
        IReadOnlyList<LowStockAlertSummaryDto> Alerts,
        int TotalCount,
        DateTimeOffset QueriedAt);

    private sealed record LowStockAlertSummaryDto(
        string Sku,
        string WarehouseId,
        int CurrentQuantity,
        int ThresholdQuantity,
        DateTimeOffset FirstDetectedAt,
        DateTimeOffset LastUpdatedAt);
}
