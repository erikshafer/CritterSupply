using Inventory.Management;

namespace Inventory.Api.IntegrationTests.Management;

/// <summary>
/// Integration tests for WarehouseSkuDetailView projection (S4 Track 1).
/// Verifies projected state matches aggregate state across full transfer lifecycle + quarantine round-trip.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class WarehouseSkuDetailViewTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public WarehouseSkuDetailViewTests(TestFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ---------------------------------------------------------------------------
    // Basic: Initialize + verify view matches aggregate
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Initialized_View_Matches_Aggregate_State()
    {
        var sku = "DETAIL-INIT-001";
        var wh = "WH-DV";
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, wh, 100));

        await using var session = _fixture.GetDocumentSession();
        var id = InventoryStreamId.Compute(sku, wh);
        var aggregate = await session.LoadAsync<ProductInventory>(id);
        var view = await session.LoadAsync<WarehouseSkuDetailView>(id);

        aggregate.ShouldNotBeNull();
        view.ShouldNotBeNull();

        view.Sku.ShouldBe(sku);
        view.WarehouseId.ShouldBe(wh);
        view.AvailableQuantity.ShouldBe(aggregate.AvailableQuantity);
        view.ReservedQuantity.ShouldBe(aggregate.ReservedQuantity);
        view.CommittedQuantity.ShouldBe(aggregate.CommittedQuantity);
        view.PickedQuantity.ShouldBe(aggregate.PickedQuantity);
        view.QuarantinedQuantity.ShouldBe(aggregate.QuarantinedQuantity);
    }

    // ---------------------------------------------------------------------------
    // Full transfer lifecycle: request → ship → receive
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Transfer_Lifecycle_Source_And_Destination_Views_Updated()
    {
        var sku = "DETAIL-TRANSFER-001";
        var sourceWh = "WH-DT-SRC";
        var destWh = "WH-DT-DST";

        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, sourceWh, 100));
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, destWh, 30));

        // Request transfer of 40
        await _fixture.ExecuteAndWaitAsync(
            new RequestTransfer(sku, sourceWh, destWh, 40, "ops@test.com"));

        await using var session1 = _fixture.GetDocumentSession();
        var sourceId = InventoryStreamId.Compute(sku, sourceWh);
        var destId = InventoryStreamId.Compute(sku, destWh);

        // Source view: available decreased, in-transit increased
        var sourceView = await session1.LoadAsync<WarehouseSkuDetailView>(sourceId);
        sourceView.ShouldNotBeNull();
        sourceView.AvailableQuantity.ShouldBe(60); // 100 - 40
        sourceView.InTransitOutQuantity.ShouldBe(40);

        // Source aggregate: available decreased
        var sourceAgg = await session1.LoadAsync<ProductInventory>(sourceId);
        sourceAgg.ShouldNotBeNull();
        sourceAgg.AvailableQuantity.ShouldBe(60);

        // Find transfer ID
        var sourceEvents = await session1.Events.FetchStreamAsync(sourceId);
        var transferId = sourceEvents.Select(e => e.Data)
            .OfType<StockTransferredOut>().First().TransferId;

        // Ship + Receive
        await _fixture.ExecuteAndWaitAsync(new ShipTransfer(transferId, "shipper@test.com"));
        await _fixture.ExecuteAndWaitAsync(new ReceiveTransfer(transferId, 40, "receiver@test.com"));

        await using var session2 = _fixture.GetDocumentSession();

        // Destination view: available increased
        var destView = await session2.LoadAsync<WarehouseSkuDetailView>(destId);
        destView.ShouldNotBeNull();
        destView.AvailableQuantity.ShouldBe(70); // 30 + 40

        // Destination aggregate matches
        var destAgg = await session2.LoadAsync<ProductInventory>(destId);
        destAgg.ShouldNotBeNull();
        destAgg.AvailableQuantity.ShouldBe(70);
    }

    // ---------------------------------------------------------------------------
    // Quarantine round-trip: quarantine → release restores available
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Quarantine_Release_RoundTrip_View_Matches_Aggregate()
    {
        var sku = "DETAIL-QUAR-001";
        var wh = "WH-DQ";
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, wh, 200));

        // Quarantine 50
        await _fixture.ExecuteAndWaitAsync(
            new QuarantineStock(sku, wh, 50, "Batch test", "clerk-a"));

        await using var session1 = _fixture.GetDocumentSession();
        var id = InventoryStreamId.Compute(sku, wh);

        var view1 = await session1.LoadAsync<WarehouseSkuDetailView>(id);
        var agg1 = await session1.LoadAsync<ProductInventory>(id);

        view1.ShouldNotBeNull();
        agg1.ShouldNotBeNull();
        view1.AvailableQuantity.ShouldBe(agg1.AvailableQuantity); // 150
        view1.QuarantinedQuantity.ShouldBe(agg1.QuarantinedQuantity); // 50

        // Find quarantine ID
        var events = await session1.Events.FetchStreamAsync(id);
        var qId = events.Select(e => e.Data).OfType<StockQuarantined>().First().QuarantineId;

        // Release all 50
        await _fixture.ExecuteAndWaitAsync(
            new ReleaseQuarantine(sku, wh, qId, 50, "clerk-b"));

        await using var session2 = _fixture.GetDocumentSession();
        var view2 = await session2.LoadAsync<WarehouseSkuDetailView>(id);
        var agg2 = await session2.LoadAsync<ProductInventory>(id);

        view2.ShouldNotBeNull();
        agg2.ShouldNotBeNull();
        view2.AvailableQuantity.ShouldBe(200); // fully restored
        view2.AvailableQuantity.ShouldBe(agg2.AvailableQuantity);
        view2.QuarantinedQuantity.ShouldBe(0);
        view2.QuarantinedQuantity.ShouldBe(agg2.QuarantinedQuantity);
    }

    // ---------------------------------------------------------------------------
    // Quarantine → dispose permanently removes stock
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Quarantine_Dispose_View_Shows_Permanent_Removal()
    {
        var sku = "DETAIL-DISPOSE-001";
        var wh = "WH-DD";
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, wh, 100));

        // Quarantine 25
        await _fixture.ExecuteAndWaitAsync(
            new QuarantineStock(sku, wh, 25, "Contaminated", "clerk-a"));

        await using var session1 = _fixture.GetDocumentSession();
        var id = InventoryStreamId.Compute(sku, wh);
        var events = await session1.Events.FetchStreamAsync(id);
        var qId = events.Select(e => e.Data).OfType<StockQuarantined>().First().QuarantineId;

        // Dispose
        await _fixture.ExecuteAndWaitAsync(
            new DisposeQuarantine(sku, wh, qId, 25, "Health hazard", "ops@test.com"));

        await using var session2 = _fixture.GetDocumentSession();
        var view = await session2.LoadAsync<WarehouseSkuDetailView>(id);
        var agg = await session2.LoadAsync<ProductInventory>(id);

        view.ShouldNotBeNull();
        agg.ShouldNotBeNull();
        view.AvailableQuantity.ShouldBe(75); // 100 - 25 (quarantine decremented)
        view.AvailableQuantity.ShouldBe(agg.AvailableQuantity);
        view.QuarantinedQuantity.ShouldBe(0); // disposed
        view.QuarantinedQuantity.ShouldBe(agg.QuarantinedQuantity);
        view.TotalOnHand.ShouldBe(75); // permanently reduced
    }

    // ---------------------------------------------------------------------------
    // Reservation + pick + ship lifecycle
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Reserve_Commit_Pick_Ship_Lifecycle_Updates_View_Buckets()
    {
        var sku = "DETAIL-LIFECYCLE-001";
        var wh = "WH-DL";
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, wh, 100));

        var orderId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var id = InventoryStreamId.Compute(sku, wh);

        // Reserve 20
        await _fixture.ExecuteAndWaitAsync(new ReserveStock(orderId, sku, wh, reservationId, 20));

        await using var s1 = _fixture.GetDocumentSession();
        var v1 = await s1.LoadAsync<WarehouseSkuDetailView>(id);
        v1.ShouldNotBeNull();
        v1.AvailableQuantity.ShouldBe(80);
        v1.ReservedQuantity.ShouldBe(20);
        v1.TotalOnHand.ShouldBe(100);

        // Commit
        await _fixture.ExecuteAndWaitAsync(new CommitReservation(id, reservationId));

        // Pick
        await _fixture.ExecuteAndWaitAsync(
            new Messages.Contracts.Fulfillment.ItemPicked(orderId, sku, wh, 20, DateTimeOffset.UtcNow));

        await using var s2 = _fixture.GetDocumentSession();
        var v2 = await s2.LoadAsync<WarehouseSkuDetailView>(id);
        v2.ShouldNotBeNull();
        v2.PickedQuantity.ShouldBe(20);

        // Ship
        await _fixture.ExecuteAndWaitAsync(
            new Messages.Contracts.Fulfillment.ShipmentHandedToCarrier(
                orderId, Guid.NewGuid(), "FedEx", "1Z999", DateTimeOffset.UtcNow));

        await using var s3 = _fixture.GetDocumentSession();
        var v3 = await s3.LoadAsync<WarehouseSkuDetailView>(id);
        var a3 = await s3.LoadAsync<ProductInventory>(id);
        v3.ShouldNotBeNull();
        a3.ShouldNotBeNull();
        v3.AvailableQuantity.ShouldBe(80);
        v3.PickedQuantity.ShouldBe(0);
        v3.TotalOnHand.ShouldBe(80); // 20 left the building
    }
}
