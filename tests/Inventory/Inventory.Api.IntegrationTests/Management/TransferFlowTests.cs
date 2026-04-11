using Inventory.Management;

namespace Inventory.Api.IntegrationTests.Management;

/// <summary>
/// Integration tests for inter-warehouse transfer lifecycle (Slices 25–29).
/// Track A: Request → Ship → Receive across two warehouses.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class TransferFlowTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public TransferFlowTests(TestFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ---------------------------------------------------------------------------
    // Slice 25: Request Transfer
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task RequestTransfer_Deducts_Source_And_Creates_Transfer()
    {
        var sku = "TRANSFER-001";
        var sourceWh = "WH-SOURCE";
        var destWh = "WH-DEST";

        // Initialize source warehouse with 100 units
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, sourceWh, 100));
        // Initialize destination warehouse with 50 units
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, destWh, 50));

        // Request transfer of 30 units
        await _fixture.ExecuteAndWaitAsync(
            new RequestTransfer(sku, sourceWh, destWh, 30, "ops@test.com"));

        await using var session = _fixture.GetDocumentSession();

        // Source should have 70 available (100 - 30)
        var sourceId = InventoryStreamId.Compute(sku, sourceWh);
        var source = await session.LoadAsync<ProductInventory>(sourceId);
        source.ShouldNotBeNull();
        source.AvailableQuantity.ShouldBe(70);

        // Source stream should have StockTransferredOut event
        var sourceEvents = await session.Events.FetchStreamAsync(sourceId);
        sourceEvents.ShouldContain(e => e.EventType == typeof(StockTransferredOut));
    }

    [Fact]
    public async Task RequestTransfer_InsufficientStock_Rejected()
    {
        var sku = "TRANSFER-INSUF";
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, "WH-01", 10));

        // Should fail — requesting 50 from 10 available
        var tracked = await _fixture.ExecuteAndWaitAsync(
            new RequestTransfer(sku, "WH-01", "WH-02", 50, "ops@test.com"));

        await using var session = _fixture.GetDocumentSession();
        var sourceId = InventoryStreamId.Compute(sku, "WH-01");
        var source = await session.LoadAsync<ProductInventory>(sourceId);
        source.ShouldNotBeNull();
        source.AvailableQuantity.ShouldBe(10); // unchanged
    }

    // ---------------------------------------------------------------------------
    // Slices 25 + 26 + 27: Full transfer happy path
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task FullTransfer_Request_Ship_Receive_Updates_Both_Warehouses()
    {
        var sku = "TRANSFER-FULL";
        var sourceWh = "WH-FULL-SRC";
        var destWh = "WH-FULL-DST";

        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, sourceWh, 100));
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, destWh, 20));

        // Step 1: Request transfer
        await _fixture.ExecuteAndWaitAsync(
            new RequestTransfer(sku, sourceWh, destWh, 40, "ops@test.com"));

        // Find the transfer
        await using var session1 = _fixture.GetDocumentSession();
        var sourceId = InventoryStreamId.Compute(sku, sourceWh);
        var sourceEvents = await session1.Events.FetchStreamAsync(sourceId);
        var transferOutEvent = sourceEvents
            .Select(e => e.Data)
            .OfType<StockTransferredOut>()
            .First();

        var transferId = transferOutEvent.TransferId;

        // Step 2: Ship transfer
        await _fixture.ExecuteAndWaitAsync(
            new ShipTransfer(transferId, "shipper@test.com"));

        // Step 3: Receive transfer (full quantity)
        await _fixture.ExecuteAndWaitAsync(
            new ReceiveTransfer(transferId, 40, "receiver@test.com"));

        await using var session2 = _fixture.GetDocumentSession();

        // Source: 100 - 40 = 60
        var source = await session2.LoadAsync<ProductInventory>(sourceId);
        source.ShouldNotBeNull();
        source.AvailableQuantity.ShouldBe(60);

        // Destination: 20 + 40 = 60
        var destId = InventoryStreamId.Compute(sku, destWh);
        var dest = await session2.LoadAsync<ProductInventory>(destId);
        dest.ShouldNotBeNull();
        dest.AvailableQuantity.ShouldBe(60);

        // Transfer aggregate should be Received
        var transfer = await session2.LoadAsync<InventoryTransfer>(transferId);
        transfer.ShouldNotBeNull();
        transfer.Status.ShouldBe(TransferStatus.Received);
        transfer.ReceivedQuantity.ShouldBe(40);
    }

    // ---------------------------------------------------------------------------
    // Slice 28: Cancel transfer (pre-ship compensation)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CancelTransfer_PreShip_Restores_Source_Stock()
    {
        var sku = "TRANSFER-CANCEL";
        var sourceWh = "WH-CANCEL-SRC";
        var destWh = "WH-CANCEL-DST";

        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, sourceWh, 100));

        // Request transfer
        await _fixture.ExecuteAndWaitAsync(
            new RequestTransfer(sku, sourceWh, destWh, 25, "ops@test.com"));

        // Get transfer ID
        await using var session1 = _fixture.GetDocumentSession();
        var sourceId = InventoryStreamId.Compute(sku, sourceWh);
        var sourceEvents = await session1.Events.FetchStreamAsync(sourceId);
        var transferId = sourceEvents
            .Select(e => e.Data)
            .OfType<StockTransferredOut>()
            .First().TransferId;

        // Source should be 75 (100 - 25)
        var source1 = await session1.LoadAsync<ProductInventory>(sourceId);
        source1!.AvailableQuantity.ShouldBe(75);

        // Cancel transfer
        await _fixture.ExecuteAndWaitAsync(
            new CancelTransfer(transferId, "No longer needed", "ops@test.com"));

        await using var session2 = _fixture.GetDocumentSession();

        // Source restored: 75 + 25 = 100 (via InventoryAdjusted compensation)
        var source2 = await session2.LoadAsync<ProductInventory>(sourceId);
        source2.ShouldNotBeNull();
        source2.AvailableQuantity.ShouldBe(100);

        // Transfer should be cancelled
        var transfer = await session2.LoadAsync<InventoryTransfer>(transferId);
        transfer.ShouldNotBeNull();
        transfer.Status.ShouldBe(TransferStatus.Cancelled);
    }

    [Fact]
    public async Task CancelTransfer_PostShip_Rejected()
    {
        var sku = "TRANSFER-CANCEL-LATE";
        var sourceWh = "WH-CL-SRC";
        var destWh = "WH-CL-DST";

        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, sourceWh, 100));

        await _fixture.ExecuteAndWaitAsync(
            new RequestTransfer(sku, sourceWh, destWh, 20, "ops@test.com"));

        await using var session1 = _fixture.GetDocumentSession();
        var sourceId = InventoryStreamId.Compute(sku, sourceWh);
        var sourceEvents = await session1.Events.FetchStreamAsync(sourceId);
        var transferId = sourceEvents.Select(e => e.Data).OfType<StockTransferredOut>().First().TransferId;

        // Ship first
        await _fixture.ExecuteAndWaitAsync(new ShipTransfer(transferId, "shipper@test.com"));

        // Try to cancel — should fail (status is Shipped)
        var tracked = await _fixture.ExecuteAndWaitAsync(
            new CancelTransfer(transferId, "Too late", "ops@test.com"));

        await using var session2 = _fixture.GetDocumentSession();
        var transfer = await session2.LoadAsync<InventoryTransfer>(transferId);
        transfer.ShouldNotBeNull();
        transfer.Status.ShouldBe(TransferStatus.Shipped); // Not cancelled
    }

    // ---------------------------------------------------------------------------
    // Slice 29: Short transfer receipt
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ShortReceipt_Produces_Discrepancy_And_Partial_Stock()
    {
        var sku = "TRANSFER-SHORT";
        var sourceWh = "WH-SHORT-SRC";
        var destWh = "WH-SHORT-DST";

        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, sourceWh, 100));
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, destWh, 10));

        // Request + Ship
        await _fixture.ExecuteAndWaitAsync(
            new RequestTransfer(sku, sourceWh, destWh, 50, "ops@test.com"));

        await using var session1 = _fixture.GetDocumentSession();
        var sourceId = InventoryStreamId.Compute(sku, sourceWh);
        var sourceEvents = await session1.Events.FetchStreamAsync(sourceId);
        var transferId = sourceEvents.Select(e => e.Data).OfType<StockTransferredOut>().First().TransferId;

        await _fixture.ExecuteAndWaitAsync(new ShipTransfer(transferId, "shipper@test.com"));

        // Receive only 40 of 50 shipped (short receipt)
        await _fixture.ExecuteAndWaitAsync(new ReceiveTransfer(transferId, 40, "receiver@test.com"));

        await using var session2 = _fixture.GetDocumentSession();

        // Destination: 10 + 40 = 50 (only received quantity added)
        var destId = InventoryStreamId.Compute(sku, destWh);
        var dest = await session2.LoadAsync<ProductInventory>(destId);
        dest.ShouldNotBeNull();
        dest.AvailableQuantity.ShouldBe(50);

        // Transfer: received, with short receipt
        var transfer = await session2.LoadAsync<InventoryTransfer>(transferId);
        transfer.ShouldNotBeNull();
        transfer.Status.ShouldBe(TransferStatus.Received);
        transfer.ReceivedQuantity.ShouldBe(40);

        // Destination stream should have StockDiscrepancyFound
        var destEvents = await session2.Events.FetchStreamAsync(destId);
        var discrepancy = destEvents
            .Select(e => e.Data)
            .OfType<StockDiscrepancyFound>()
            .FirstOrDefault();

        discrepancy.ShouldNotBeNull();
        discrepancy.DiscrepancyType.ShouldBe(DiscrepancyType.ShortTransfer);
        discrepancy.ExpectedQuantity.ShouldBe(50);
        discrepancy.ActualQuantity.ShouldBe(40);
    }

    // ---------------------------------------------------------------------------
    // Slice 26: Ship status guard
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ShipTransfer_WhenNotRequested_Rejected()
    {
        var sku = "TRANSFER-SHIP-GUARD";
        var sourceWh = "WH-SG-SRC";
        var destWh = "WH-SG-DST";

        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, sourceWh, 100));

        await _fixture.ExecuteAndWaitAsync(
            new RequestTransfer(sku, sourceWh, destWh, 20, "ops@test.com"));

        await using var session1 = _fixture.GetDocumentSession();
        var sourceId = InventoryStreamId.Compute(sku, sourceWh);
        var sourceEvents = await session1.Events.FetchStreamAsync(sourceId);
        var transferId = sourceEvents.Select(e => e.Data).OfType<StockTransferredOut>().First().TransferId;

        // Ship once (valid)
        await _fixture.ExecuteAndWaitAsync(new ShipTransfer(transferId, "shipper@test.com"));

        // Ship again (invalid — already shipped)
        await _fixture.ExecuteAndWaitAsync(new ShipTransfer(transferId, "shipper2@test.com"));

        await using var session2 = _fixture.GetDocumentSession();
        var transfer = await session2.LoadAsync<InventoryTransfer>(transferId);
        transfer.ShouldNotBeNull();
        transfer.Status.ShouldBe(TransferStatus.Shipped); // Still shipped, second ship rejected
    }
}
