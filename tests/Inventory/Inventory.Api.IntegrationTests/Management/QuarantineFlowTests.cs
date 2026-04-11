using Inventory.Management;

namespace Inventory.Api.IntegrationTests.Management;

/// <summary>
/// Integration tests for quarantine lifecycle (Slices 33–35).
/// Track C: Quarantine → Release/Dispose paths.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class QuarantineFlowTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public QuarantineFlowTests(TestFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ---------------------------------------------------------------------------
    // Slice 33: QuarantineStock
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task QuarantineStock_Decrements_Available_And_Tracks_Quarantined()
    {
        var sku = "QUAR-001";
        var wh = "WH-QUAR";
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, wh, 100));

        await _fixture.ExecuteAndWaitAsync(
            new QuarantineStock(sku, wh, 20, "Suspect batch", "clerk-a"));

        await using var session = _fixture.GetDocumentSession();
        var inventoryId = InventoryStreamId.Compute(sku, wh);
        var inventory = await session.LoadAsync<ProductInventory>(inventoryId);

        inventory.ShouldNotBeNull();
        inventory.AvailableQuantity.ShouldBe(80); // 100 - 20 (via InventoryAdjusted)
        inventory.QuarantinedQuantity.ShouldBe(20);

        var events = await session.Events.FetchStreamAsync(inventoryId);
        events.ShouldContain(e => e.EventType == typeof(StockQuarantined));
        events.ShouldContain(e => e.EventType == typeof(InventoryAdjusted));
    }

    [Fact]
    public async Task QuarantineStock_InsufficientAvailable_Rejected()
    {
        var sku = "QUAR-INSUF";
        var wh = "WH-QINSUF";
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, wh, 10));

        // Try to quarantine more than available
        await _fixture.ExecuteAndWaitAsync(
            new QuarantineStock(sku, wh, 50, "Suspect", "clerk-a"));

        await using var session = _fixture.GetDocumentSession();
        var inventoryId = InventoryStreamId.Compute(sku, wh);
        var inventory = await session.LoadAsync<ProductInventory>(inventoryId);

        inventory.ShouldNotBeNull();
        inventory.AvailableQuantity.ShouldBe(10); // unchanged
        inventory.QuarantinedQuantity.ShouldBe(0); // nothing quarantined
    }

    // ---------------------------------------------------------------------------
    // Slice 34: ReleaseQuarantine (quarantine → release restores available)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task QuarantineAndRelease_Restores_AvailableQuantity()
    {
        var sku = "QUAR-RELEASE";
        var wh = "WH-QREL";
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, wh, 100));

        // Quarantine 30
        await _fixture.ExecuteAndWaitAsync(
            new QuarantineStock(sku, wh, 30, "Inspection", "clerk-a"));

        await using var session1 = _fixture.GetDocumentSession();
        var inventoryId = InventoryStreamId.Compute(sku, wh);
        var inv1 = await session1.LoadAsync<ProductInventory>(inventoryId);
        inv1!.AvailableQuantity.ShouldBe(70);
        inv1.QuarantinedQuantity.ShouldBe(30);

        // Find quarantine ID from events
        var events = await session1.Events.FetchStreamAsync(inventoryId);
        var quarantineEvent = events.Select(e => e.Data).OfType<StockQuarantined>().First();

        // Release 30
        await _fixture.ExecuteAndWaitAsync(
            new ReleaseQuarantine(sku, wh, quarantineEvent.QuarantineId, 30, "clerk-b"));

        await using var session2 = _fixture.GetDocumentSession();
        var inv2 = await session2.LoadAsync<ProductInventory>(inventoryId);

        inv2.ShouldNotBeNull();
        inv2.AvailableQuantity.ShouldBe(100); // Restored
        inv2.QuarantinedQuantity.ShouldBe(0);
    }

    // ---------------------------------------------------------------------------
    // Slice 35: DisposeQuarantine (quarantine → dispose = permanent removal)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task QuarantineAndDispose_Permanently_Removes_Stock()
    {
        var sku = "QUAR-DISPOSE";
        var wh = "WH-QDISP";
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, wh, 100));

        // Quarantine 25
        await _fixture.ExecuteAndWaitAsync(
            new QuarantineStock(sku, wh, 25, "Contaminated", "clerk-a"));

        await using var session1 = _fixture.GetDocumentSession();
        var inventoryId = InventoryStreamId.Compute(sku, wh);
        var events = await session1.Events.FetchStreamAsync(inventoryId);
        var quarantineEvent = events.Select(e => e.Data).OfType<StockQuarantined>().First();

        // Dispose
        await _fixture.ExecuteAndWaitAsync(
            new DisposeQuarantine(sku, wh, quarantineEvent.QuarantineId, 25, "Health hazard", "ops@test.com"));

        await using var session2 = _fixture.GetDocumentSession();
        var inv = await session2.LoadAsync<ProductInventory>(inventoryId);

        inv.ShouldNotBeNull();
        // Available was already decremented during quarantine (75), stays at 75
        inv.AvailableQuantity.ShouldBe(75);
        // Quarantined was 25, now 0 (disposed)
        inv.QuarantinedQuantity.ShouldBe(0);

        // Should have StockWrittenOff event
        var events2 = await session2.Events.FetchStreamAsync(inventoryId);
        events2.ShouldContain(e => e.EventType == typeof(QuarantineDisposed));
        events2.ShouldContain(e => e.EventType == typeof(StockWrittenOff));
    }

    [Fact]
    public async Task DisposeQuarantine_ExceedsQuarantined_Rejected()
    {
        var sku = "QUAR-DISP-OVER";
        var wh = "WH-QDOVER";
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, wh, 100));

        // Quarantine 10
        await _fixture.ExecuteAndWaitAsync(
            new QuarantineStock(sku, wh, 10, "Test", "clerk-a"));

        // Try to dispose 50 from quarantine (only 10 there)
        await _fixture.ExecuteAndWaitAsync(
            new DisposeQuarantine(sku, wh, Guid.NewGuid(), 50, "Over-disposal", "ops@test.com"));

        await using var session = _fixture.GetDocumentSession();
        var inventoryId = InventoryStreamId.Compute(sku, wh);
        var inv = await session.LoadAsync<ProductInventory>(inventoryId);

        inv.ShouldNotBeNull();
        inv.QuarantinedQuantity.ShouldBe(10); // unchanged — disposal rejected
    }

    // ---------------------------------------------------------------------------
    // Round-trip: quarantine → release confirms no stock loss
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task QuarantineRelease_RoundTrip_NoStockLoss()
    {
        var sku = "QUAR-ROUNDTRIP";
        var wh = "WH-QRT";
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, wh, 200));

        // Quarantine 50
        await _fixture.ExecuteAndWaitAsync(
            new QuarantineStock(sku, wh, 50, "Batch test", "clerk-a"));

        await using var s1 = _fixture.GetDocumentSession();
        var inventoryId = InventoryStreamId.Compute(sku, wh);
        var events = await s1.Events.FetchStreamAsync(inventoryId);
        var qId = events.Select(e => e.Data).OfType<StockQuarantined>().First().QuarantineId;

        // Release all 50
        await _fixture.ExecuteAndWaitAsync(
            new ReleaseQuarantine(sku, wh, qId, 50, "clerk-b"));

        await using var s2 = _fixture.GetDocumentSession();
        var inv = await s2.LoadAsync<ProductInventory>(inventoryId);

        inv.ShouldNotBeNull();
        inv.AvailableQuantity.ShouldBe(200); // fully restored
        inv.QuarantinedQuantity.ShouldBe(0);
        inv.TotalOnHand.ShouldBe(200);
    }
}
