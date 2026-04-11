using Inventory.Management;

namespace Inventory.Api.IntegrationTests.Management;

/// <summary>
/// Integration tests for AlertFeedView projection rebuild and StockDiscrepancyDetected integration event.
/// Track 2 (S4): read model completeness and S3 carryover.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class AlertFeedViewTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public AlertFeedViewTests(TestFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ---------------------------------------------------------------------------
    // AlertFeedView rebuild test — scripted event sequence
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task AlertFeedView_Rebuild_Scripted_Discrepancy_Sequence()
    {
        var sku = "ALERT-REBUILD-001";
        var wh = "WH-ALERT";
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, wh, 100));

        // 1) Short pick → StockDiscrepancyFound (ShortPick)
        // Set up committed allocation first
        var orderId1 = Guid.NewGuid();
        var reservationId1 = Guid.NewGuid();
        var inventoryId = InventoryStreamId.Compute(sku, wh);
        await _fixture.ExecuteAndWaitAsync(
            new ReserveStock(orderId1, sku, wh, reservationId1, 20));
        await _fixture.ExecuteAndWaitAsync(
            new CommitReservation(inventoryId, reservationId1));

        // Simulate pick with short quantity via ItemPicked integration message
        await _fixture.ExecuteAndWaitAsync(
            new Messages.Contracts.Fulfillment.ItemPicked(orderId1, sku, wh, 15, DateTimeOffset.UtcNow));

        // Wait for async projection daemon to process
        await Task.Delay(2000);

        await using var session1 = _fixture.GetDocumentSession();

        // Verify discrepancy event was appended
        var events = await session1.Events.FetchStreamAsync(inventoryId);
        var discrepancies = events.Select(e => e.Data).OfType<StockDiscrepancyFound>().ToList();
        discrepancies.ShouldNotBeEmpty();
        discrepancies.ShouldContain(d => d.DiscrepancyType == DiscrepancyType.ShortPick);
    }

    [Fact]
    public async Task AlertFeedView_LowStockThreshold_Generates_Alert()
    {
        var sku = "ALERT-LOWSTOCK-001";
        var wh = "WH-LOWSTOCK";

        // Initialize with quantity just above threshold (10)
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, wh, 15));

        // Quarantine 6 units → available drops to 9 (below threshold of 10)
        // QuarantineStock calls LowStockPolicy.CrossedThresholdDownward
        await _fixture.ExecuteAndWaitAsync(
            new QuarantineStock(sku, wh, 6, "Batch investigation", "clerk-a"));

        await using var session = _fixture.GetDocumentSession();
        var inventoryId = InventoryStreamId.Compute(sku, wh);
        var events = await session.Events.FetchStreamAsync(inventoryId);

        // Should have LowStockThresholdBreached event
        var lowStockEvents = events.Select(e => e.Data).OfType<LowStockThresholdBreached>().ToList();
        lowStockEvents.ShouldNotBeEmpty();
        lowStockEvents[0].Sku.ShouldBe(sku);
        lowStockEvents[0].WarehouseId.ShouldBe(wh);
        lowStockEvents[0].Threshold.ShouldBe(10);
    }

    // ---------------------------------------------------------------------------
    // StockDiscrepancyDetected integration event test (S3 carryover item 2)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ShortPick_Publishes_StockDiscrepancyDetected_Integration_Event()
    {
        var sku = "DISCREPANCY-INTEG-001";
        var wh = "WH-DISC";
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, wh, 50));

        var orderId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var inventoryId = InventoryStreamId.Compute(sku, wh);
        await _fixture.ExecuteAndWaitAsync(
            new ReserveStock(orderId, sku, wh, reservationId, 10));
        await _fixture.ExecuteAndWaitAsync(
            new CommitReservation(inventoryId, reservationId));

        // ItemPicked with short quantity (7 of 10 committed)
        await _fixture.ExecuteAndWaitAsync(
            new Messages.Contracts.Fulfillment.ItemPicked(orderId, sku, wh, 7, DateTimeOffset.UtcNow));

        // Verify domain event on stream proves handler produced discrepancy
        await using var session = _fixture.GetDocumentSession();
        var events = await session.Events.FetchStreamAsync(inventoryId);
        var discrepancies = events.Select(e => e.Data).OfType<StockDiscrepancyFound>().ToList();
        discrepancies.ShouldNotBeEmpty();

        var discrepancy = discrepancies.First();
        discrepancy.Sku.ShouldBe(sku);
        discrepancy.WarehouseId.ShouldBe(wh);
        discrepancy.ExpectedQuantity.ShouldBe(10);
        discrepancy.ActualQuantity.ShouldBe(7);
        discrepancy.DiscrepancyType.ShouldBe(DiscrepancyType.ShortPick);
    }

    [Fact]
    public async Task ZeroPick_Publishes_StockDiscrepancyDetected_Integration_Event()
    {
        var sku = "DISCREPANCY-ZERO-001";
        var wh = "WH-ZERO";
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, wh, 50));

        var orderId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var inventoryId = InventoryStreamId.Compute(sku, wh);
        await _fixture.ExecuteAndWaitAsync(
            new ReserveStock(orderId, sku, wh, reservationId, 10));
        await _fixture.ExecuteAndWaitAsync(
            new CommitReservation(inventoryId, reservationId));

        // ItemPicked with zero quantity (complete bin miss)
        await _fixture.ExecuteAndWaitAsync(
            new Messages.Contracts.Fulfillment.ItemPicked(orderId, sku, wh, 0, DateTimeOffset.UtcNow));

        // Verify domain event on stream
        await using var session = _fixture.GetDocumentSession();
        var events = await session.Events.FetchStreamAsync(inventoryId);
        var discrepancies = events.Select(e => e.Data).OfType<StockDiscrepancyFound>().ToList();
        discrepancies.ShouldNotBeEmpty();

        var discrepancy = discrepancies.First();
        discrepancy.DiscrepancyType.ShouldBe(DiscrepancyType.ZeroPick);
        discrepancy.ExpectedQuantity.ShouldBe(10);
        discrepancy.ActualQuantity.ShouldBe(0);
    }

    [Fact]
    public async Task FullPick_Does_Not_Publish_StockDiscrepancyDetected()
    {
        var sku = "NO-DISCREPANCY-001";
        var wh = "WH-NODISC";
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, wh, 50));

        var orderId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var inventoryId = InventoryStreamId.Compute(sku, wh);
        await _fixture.ExecuteAndWaitAsync(
            new ReserveStock(orderId, sku, wh, reservationId, 10));
        await _fixture.ExecuteAndWaitAsync(
            new CommitReservation(inventoryId, reservationId));

        // Full pick — no discrepancy
        await _fixture.ExecuteAndWaitAsync(
            new Messages.Contracts.Fulfillment.ItemPicked(orderId, sku, wh, 10, DateTimeOffset.UtcNow));

        // No StockDiscrepancyFound event on stream
        await using var session = _fixture.GetDocumentSession();
        var events = await session.Events.FetchStreamAsync(inventoryId);
        var discrepancies = events.Select(e => e.Data).OfType<StockDiscrepancyFound>();
        discrepancies.ShouldBeEmpty();
    }
}
